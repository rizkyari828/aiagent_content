using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Approvals;

/// <summary>
/// A3 final approval workflow. The decision has two directions:
/// <c>approved</c> is possible only when the RenderVideo job succeeded, a FinalVideoQa
/// job for that exact render succeeded with pass evidence, and the artifact re-read
/// through <see cref="IAssetFileStore"/> matches the recorded render hash and size;
/// <c>changes_requested</c> only needs an existing render job with usable evidence, so an
/// operator can refuse a failed QA result without first fixing the render. Validity is
/// re-evaluated on every read so a changed artifact, invalidated QA evidence, or a newer
/// render never leaves a stale approval "approved".
/// </summary>
public sealed class FinalApprovalWorkflow(
    IFinalApprovalRepository approvals,
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IAssetFileStore fileStore,
    TimeProvider timeProvider)
{
    /// <summary>Approves the render; this is the legacy behavior and requires the full gate.</summary>
    public Task<FinalApprovalApproveResult?> ApproveAsync(
        Guid contentProjectId,
        Guid renderJobId,
        string destination,
        string? notes,
        string? approverLabel,
        CancellationToken cancellationToken) =>
        DecideAsync(
            contentProjectId,
            renderJobId,
            FinalApprovalDecision.Approved,
            null,
            destination,
            notes,
            approverLabel,
            cancellationToken);

    /// <summary>
    /// Records <c>changes_requested</c> with a mandatory note. Deliberately skips the QA,
    /// supersession, and artifact-hash gates: a failed QA is exactly when an operator must
    /// still be able to reject the result. The render job must exist and expose usable
    /// evidence because the approval row is always bound to one render artifact.
    /// </summary>
    public Task<FinalApprovalApproveResult?> RequestChangesAsync(
        Guid contentProjectId,
        Guid renderJobId,
        string destination,
        string note,
        string? approverLabel,
        CancellationToken cancellationToken) =>
        DecideAsync(
            contentProjectId,
            renderJobId,
            FinalApprovalDecision.ChangesRequested,
            note,
            destination,
            null,
            approverLabel,
            cancellationToken);

    private async Task<FinalApprovalApproveResult?> DecideAsync(
        Guid contentProjectId,
        Guid renderJobId,
        FinalApprovalDecision decision,
        string? decisionNote,
        string destination,
        string? notes,
        string? approverLabel,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(renderJobId, nameof(renderJobId));

        var project = await contentProjects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var renderJob = await jobs.FindByIdAsync(renderJobId, cancellationToken);
        if (renderJob is null || renderJob.ContentProjectId != contentProjectId)
        {
            throw Error(
                "final_approval_render_missing",
                $"RenderVideo job '{renderJobId}' does not exist for this content project.");
        }

        var renderResult = decision == FinalApprovalDecision.ChangesRequested
            ? ReadRenderResult(
                renderJob,
                "Requesting changes requires a completed RenderVideo job with usable evidence.")
            : ReadRenderResult(renderJob);

        string artifactPath;
        string artifactHash;
        long artifactByteSize;
        if (decision == FinalApprovalDecision.ChangesRequested)
        {
            // The rejection is recorded against the render evidence itself. The artifact
            // is not re-read: a missing/changed file must not prevent an operator from
            // refusing the result.
            artifactPath = renderResult.OutputPath;
            artifactHash = renderResult.ContentHash;
            artifactByteSize = renderResult.ByteSize;
        }
        else
        {
            var qaEvidence = await approvals.FindLatestQaAsync(
                contentProjectId,
                renderJobId,
                cancellationToken);
            if (qaEvidence is null || !IsPassingQa(qaEvidence, renderResult.ContentHash))
            {
                throw Error(
                    "final_approval_qa_required",
                    "A succeeded FinalVideoQa job with pass evidence is required for this render.");
            }

            if (await approvals.HasNewerRenderAsync(
                contentProjectId,
                renderJobId,
                renderJob.CreatedAt,
                cancellationToken))
            {
                throw Error(
                    "final_approval_render_superseded",
                    "A newer RenderVideo job exists for this content project; approve the current render.");
            }

            var artifact = ReadArtifact(renderResult.OutputPath);
            if (!string.Equals(
                artifact.ContentHash,
                renderResult.ContentHash,
                StringComparison.OrdinalIgnoreCase))
            {
                throw Error(
                    "final_approval_artifact_mismatch",
                    $"'{renderResult.OutputPath}' does not match the recorded render hash.");
            }

            if (artifact.ByteSize != renderResult.ByteSize)
            {
                throw Error(
                    "final_approval_artifact_mismatch",
                    $"'{renderResult.OutputPath}' does not match the recorded render byte size.");
            }

            artifactPath = artifact.RelativePath;
            artifactHash = artifact.ContentHash;
            artifactByteSize = artifact.ByteSize;
        }

        var existing = await approvals.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (existing is null)
        {
            var created = FinalApproval.Create(
                contentProjectId,
                renderJobId,
                artifactPath,
                artifactHash,
                artifactByteSize,
                destination,
                notes,
                approverLabel,
                now,
                decision: decision,
                decisionNote: decisionNote);
            approvals.Add(created);
            await approvals.SaveChangesAsync(cancellationToken);
            return new FinalApprovalApproveResult(true, ToSnapshot(created));
        }

        if (existing.Apply(
            renderJobId,
            artifactPath,
            artifactHash,
            artifactByteSize,
            destination,
            notes,
            approverLabel,
            now,
            decision,
            decisionNote))
        {
            await approvals.SaveChangesAsync(cancellationToken);
        }

        return new FinalApprovalApproveResult(false, ToSnapshot(existing));
    }

    /// <summary>
    /// Returns <c>null</c> when the content project does not exist; otherwise a
    /// deterministic approved/invalid/changes_requested/missing status with a stable
    /// reason code.
    /// </summary>
    public async Task<FinalApprovalStatus?> EvaluateAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        var project = await contentProjects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var approval = await approvals.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        if (approval is null)
        {
            return FinalApprovalStatus.Missing();
        }

        var snapshot = ToSnapshot(approval);
        if (approval.Decision == FinalApprovalDecision.ChangesRequested)
        {
            // A rejection is an operator decision, not an evidence claim: later render,
            // QA, or artifact changes do not turn it back into an approval.
            return FinalApprovalStatus.ChangesRequested(snapshot);
        }

        var renderJob = await jobs.FindByIdAsync(
            approval.SourceRenderJobId,
            cancellationToken);
        if (renderJob is null || renderJob.ContentProjectId != contentProjectId)
        {
            return FinalApprovalStatus.Invalid(
                "final_approval_render_missing",
                snapshot);
        }

        RenderVideoResult renderResult;
        try
        {
            renderResult = ReadRenderResult(renderJob);
        }
        catch (FinalApprovalException)
        {
            return FinalApprovalStatus.Invalid(
                "final_approval_render_not_succeeded",
                snapshot);
        }

        if (!string.Equals(
                approval.ArtifactPath,
                renderResult.OutputPath,
                StringComparison.Ordinal)
            || !string.Equals(
                approval.ArtifactContentHash,
                renderResult.ContentHash,
                StringComparison.OrdinalIgnoreCase)
            || approval.ArtifactByteSize != renderResult.ByteSize)
        {
            return FinalApprovalStatus.Invalid(
                "final_approval_artifact_mismatch",
                snapshot);
        }

        var qaEvidence = await approvals.FindLatestQaAsync(
            contentProjectId,
            approval.SourceRenderJobId,
            cancellationToken);
        if (qaEvidence is null || !IsPassingQa(qaEvidence, renderResult.ContentHash))
        {
            return FinalApprovalStatus.Invalid(
                "final_approval_qa_invalidated",
                snapshot);
        }

        AssetFileInfo artifact;
        try
        {
            artifact = ReadArtifact(approval.ArtifactPath);
        }
        catch (FinalApprovalException exception)
        {
            return FinalApprovalStatus.Invalid(exception.ErrorCode, snapshot);
        }

        if (!string.Equals(
                artifact.ContentHash,
                approval.ArtifactContentHash,
                StringComparison.OrdinalIgnoreCase)
            || artifact.ByteSize != approval.ArtifactByteSize)
        {
            return FinalApprovalStatus.Invalid(
                "final_approval_artifact_mismatch",
                snapshot);
        }

        if (await approvals.HasNewerRenderAsync(
            contentProjectId,
            approval.SourceRenderJobId,
            renderJob.CreatedAt,
            cancellationToken))
        {
            return FinalApprovalStatus.Invalid(
                "final_approval_render_superseded",
                snapshot);
        }

        return FinalApprovalStatus.Approved(snapshot);
    }

    /// <summary>
    /// Read-only QA verdict for a render without requiring an approval row. This exists
    /// so readiness can report "render exists but QA has not passed" using the same
    /// evidence rules as A3 instead of a second interpretation of QA results.
    /// </summary>
    public async Task<FinalVideoQaVerdict> EvaluateQaAsync(
        Guid contentProjectId,
        Guid renderJobId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(renderJobId, nameof(renderJobId));

        var renderJob = await jobs.FindByIdAsync(renderJobId, cancellationToken);
        if (renderJob is null || renderJob.ContentProjectId != contentProjectId)
        {
            return FinalVideoQaVerdict.RenderNotSucceeded;
        }

        RenderVideoResult renderResult;
        try
        {
            renderResult = ReadRenderResult(renderJob);
        }
        catch (FinalApprovalException)
        {
            return FinalVideoQaVerdict.RenderNotSucceeded;
        }

        var qaEvidence = await approvals.FindLatestQaAsync(
            contentProjectId,
            renderJobId,
            cancellationToken);
        if (qaEvidence is null)
        {
            return FinalVideoQaVerdict.Missing;
        }

        return IsPassingQa(qaEvidence, renderResult.ContentHash)
            ? FinalVideoQaVerdict.Passed
            : FinalVideoQaVerdict.Failed;
    }

    internal static FinalApprovalSnapshot ToSnapshot(FinalApproval approval) =>
        new(
            approval.Id,
            approval.ContentProjectId,
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            approval.Destination,
            approval.Notes,
            approval.ApproverLabel,
            approval.Decision,
            approval.DecisionNote,
            approval.DecidedAt,
            approval.Revision,
            approval.ApprovedAt,
            approval.CreatedAt,
            approval.UpdatedAt);

    private static RenderVideoResult ReadRenderResult(
        JobSnapshot renderJob,
        string? unsucceededMessage = null)
    {
        if (renderJob.Type != JobType.RenderVideo
            || renderJob.Status != JobStatus.Succeeded
            || renderJob.Result is null)
        {
            throw Error(
                "final_approval_render_not_succeeded",
                unsucceededMessage
                    ?? "Approval requires a completed RenderVideo job result.");
        }

        try
        {
            return RenderVideoResult.Deserialize(renderJob.Result);
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "final_approval_render_not_succeeded",
                unsucceededMessage
                    ?? "The RenderVideo result is not a valid structured result.",
                exception);
        }
    }

    private static bool IsPassingQa(FinalVideoQaEvidence evidence, string renderHash)
    {
        if (!evidence.Succeeded || evidence.Result is null)
        {
            return false;
        }

        try
        {
            var result = FinalVideoQaResult.Deserialize(evidence.Result);
            return result.DurationSeconds > 0
                && result.HasVideo
                && result.HasAudio
                && string.Equals(
                    result.ContentHash,
                    renderHash,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (JobExecutionException)
        {
            return false;
        }
    }

    private AssetFileInfo ReadArtifact(string relativePath)
    {
        try
        {
            return fileStore.Register(relativePath);
        }
        catch (AssetCollectionException exception)
            when (exception.ErrorCode == "asset_file_not_found")
        {
            throw Error(
                "final_approval_artifact_missing",
                $"Approved artifact '{relativePath}' no longer exists.",
                exception);
        }
        catch (AssetCollectionException exception)
        {
            throw Error(
                "final_approval_artifact_invalid",
                $"Approved artifact '{relativePath}' is not a readable file.",
                exception);
        }
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }

    private static FinalApprovalException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
