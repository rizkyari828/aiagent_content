using AIStudio.Application.Approvals;
using AIStudio.Application.Jobs;
using AIStudio.Application.Tracking;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Content;

/// <summary>
/// Computed readiness plus explicit guarded publishing transitions.
///
/// Readiness reuses the A3 workflow for both QA evidence and approval validity; it
/// never re-implements the QA pass rules or the approval invalidation rules. The
/// status is derived on every read, so a changed artifact, a newer failed QA, a newer
/// render, or an approved/invalid approval is reflected immediately.
/// </summary>
public sealed class ContentReadinessWorkflow(
    IContentProjectRepository projects,
    IProjectJobReader jobs,
    FinalApprovalWorkflow approvals,
    IPublicationRepository publications,
    TimeProvider timeProvider,
    IPublicationReferenceLookup? publicationLookup = null)
{
    /// <summary>Returns <c>null</c> when the content project does not exist.</summary>
    public async Task<ContentReadiness?> EvaluateAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        var project = await projects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var latestJob = await jobs.FindLatestAsync(contentProjectId, cancellationToken);
        if (latestJob is null)
        {
            return Project(
                project,
                ContentReadinessStatus.NotStarted,
                Reason(
                    ContentReadinessCodes.RenderMissing,
                    "No production job has been created for this content project.",
                    blocking: true));
        }

        var latestRender = await jobs.FindLatestAsync(
            contentProjectId,
            JobType.RenderVideo,
            cancellationToken);
        if (latestRender is null || latestRender.Status != JobStatus.Succeeded)
        {
            // A terminal failed/cancelled render is a dead end until a new render is
            // enqueued; a queued/running render is simply still in progress.
            var terminalFailure = latestRender is not null
                && latestRender.Status is JobStatus.Failed or JobStatus.Cancelled;
            return Project(
                project,
                terminalFailure
                    ? ContentReadinessStatus.Blocked
                    : ContentReadinessStatus.InProgress,
                Reason(
                    ContentReadinessCodes.RenderMissing,
                    latestRender is null
                        ? "No RenderVideo job exists for this content project yet."
                        : $"The newest RenderVideo job is '{latestRender.Status}'; a succeeded render is required.",
                    blocking: true));
        }

        var approval = await approvals.EvaluateAsync(contentProjectId, cancellationToken);
        if (approval is null)
        {
            return null;
        }

        // A change request is a deliberate operator rejection. It blocks readiness even
        // when the newest QA failed (that failure is exactly why changes were requested),
        // so it is checked before the QA verdict instead of after it.
        if (approval.IsChangesRequested)
        {
            return Project(
                project,
                ContentReadinessStatus.Blocked,
                Reason(
                    ContentReadinessCodes.FinalApprovalChangesRequested,
                    "The A3 review requested changes; update the production and approve a new render.",
                    blocking: true));
        }

        var qaVerdict = await approvals.EvaluateQaAsync(
            contentProjectId,
            latestRender.Id,
            cancellationToken);
        if (qaVerdict == FinalVideoQaVerdict.RenderNotSucceeded)
        {
            return Project(
                project,
                ContentReadinessStatus.Blocked,
                Reason(
                    ContentReadinessCodes.RenderMissing,
                    "The succeeded RenderVideo job does not expose usable render evidence.",
                    blocking: true));
        }

        if (qaVerdict != FinalVideoQaVerdict.Passed)
        {
            return Project(
                project,
                ContentReadinessStatus.InProgress,
                Reason(
                    ContentReadinessCodes.QaFailed,
                    qaVerdict == FinalVideoQaVerdict.Missing
                        ? "No terminal FinalVideoQa job has evaluated the current render."
                        : "The newest FinalVideoQa job did not pass for the current render.",
                    blocking: true));
        }

        if (!approval.IsApproved)
        {
            return approval.State == FinalApprovalState.Missing
                ? Project(
                    project,
                    ContentReadinessStatus.QaPassed,
                    Reason(
                        ContentReadinessCodes.FinalApprovalMissing,
                        "QA passed, but no A3 final approval has been recorded.",
                        blocking: true))
                : Project(
                    project,
                    ContentReadinessStatus.QaPassed,
                    Reason(
                        ContentReadinessCodes.FinalApprovalInvalid,
                        $"The recorded A3 final approval is invalid ({approval.Reason}).",
                        blocking: true));
        }

        var hasPublication = await HasPublicationRecordAsync(
            contentProjectId,
            cancellationToken);

        return Project(
            project,
            ContentReadinessStatus.ReadyToPublish,
            !hasPublication
                ? Reason(
                    ContentReadinessCodes.PublicationMissing,
                    "No manual publication record exists yet; publishing remains an operator step.",
                    blocking: false)
                : null);
    }

    /// <summary>
    /// Explicit, evidence-gated transition to <see cref="ContentStatus.ReadyToPublish"/>.
    /// Repeating it is idempotent (<c>Transitioned = false</c>); a project that is not
    /// ready throws <see cref="ContentProjectException"/> with
    /// <c>content_project_not_ready</c> plus the blocking reason codes.
    /// </summary>
    public async Task<ContentProjectTransitionResult?> MarkReadyToPublishAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        var readiness = await EvaluateAsync(contentProjectId, cancellationToken);
        if (readiness is null)
        {
            return null;
        }

        if (readiness.ProjectStatus == ContentStatus.ReadyToPublish)
        {
            return new ContentProjectTransitionResult(false, readiness);
        }

        if (readiness.ProjectStatus is ContentStatus.Published or ContentStatus.Archived)
        {
            throw new ContentProjectException(
                "content_project_transition_not_allowed",
                $"Content project cannot become ReadyToPublish from {readiness.ProjectStatus}.");
        }

        if (!readiness.IsReadyToPublish)
        {
            throw new ContentProjectException(
                "content_project_not_ready",
                "The content project is not ready to publish: a passed QA and a valid A3 final approval are required.",
                readiness.BlockingReasonCodes);
        }

        var project = await projects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.MarkReadyToPublish(timeProvider.GetUtcNow());
        await projects.SaveChangesAsync(cancellationToken);

        var updated = await EvaluateAsync(contentProjectId, cancellationToken);
        return updated is null
            ? null
            : new ContentProjectTransitionResult(true, updated);
    }

    /// <summary>
    /// Explicit, evidence-gated transition to <see cref="ContentStatus.Published"/>.
    /// Repeating it is idempotent (<c>Transitioned = false</c>); a project that is not
    /// ready throws <see cref="ContentProjectException"/> with
    /// <c>content_project_not_ready</c> plus the blocking reason codes.
    /// An archived project throws <c>content_project_transition_not_allowed</c>.
    /// </summary>
    public async Task<ContentProjectTransitionResult?> MarkPublishedAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        var readiness = await EvaluateAsync(contentProjectId, cancellationToken);
        if (readiness is null)
        {
            return null;
        }

        if (readiness.ProjectStatus == ContentStatus.Published)
        {
            return new ContentProjectTransitionResult(false, readiness);
        }

        if (readiness.ProjectStatus == ContentStatus.Archived)
        {
            throw new ContentProjectException(
                "content_project_transition_not_allowed",
                $"Content project cannot become Published from {readiness.ProjectStatus}.");
        }

        var hasPublication = await HasPublicationRecordAsync(
            contentProjectId,
            cancellationToken);
        var blockingReasons = new List<string>(readiness.BlockingReasonCodes);
        if (!hasPublication && !blockingReasons.Contains(ContentReadinessCodes.PublicationMissing))
        {
            blockingReasons.Add(ContentReadinessCodes.PublicationMissing);
        }

        if (blockingReasons.Count > 0)
        {
            throw new ContentProjectException(
                "content_project_not_ready",
                "The content project is not ready to publish: a valid A3 final approval and a publication record are required.",
                blockingReasons);
        }

        if (readiness.ProjectStatus != ContentStatus.ReadyToPublish)
        {
            throw new ContentProjectException(
                "content_project_transition_not_allowed",
                $"Content project cannot become Published from {readiness.ProjectStatus}.");
        }

        var project = await projects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.MarkPublished(timeProvider.GetUtcNow());
        await projects.SaveChangesAsync(cancellationToken);

        var updated = await EvaluateAsync(contentProjectId, cancellationToken);
        return updated is null
            ? null
            : new ContentProjectTransitionResult(true, updated);
    }

    private async Task<bool> HasPublicationRecordAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        if (publicationLookup is not null)
        {
            var publicationId = await publicationLookup.FindPublicationRecordIdAsync(
                contentProjectId,
                cancellationToken);
            return publicationId is not null;
        }

        var publication = await publications.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        return publication is not null;
    }

    private static ContentReadiness Project(
        ContentProject project,
        ContentReadinessStatus status,
        ContentReadinessReason? reason) =>
        new(
            project.Id,
            project.Status,
            status,
            reason is null ? [] : [reason]);

    private static ContentReadinessReason Reason(
        string code,
        string message,
        bool blocking) =>
        new(code, message, blocking);

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }
}
