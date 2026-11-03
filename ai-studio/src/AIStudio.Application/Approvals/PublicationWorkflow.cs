using AIStudio.Application.Content;
using AIStudio.Domain.Approvals;

namespace AIStudio.Application.Approvals;

/// <summary>
/// Manual publication record workflow. It records an upload the operator already
/// performed; it never uploads, authenticates, or contacts a platform. Creation
/// requires a currently valid A3 approval for the same destination, and repeating the
/// exact same record is idempotent instead of inserting a duplicate.
/// </summary>
public sealed class PublicationWorkflow(
    IPublicationRepository publications,
    IContentProjectReader contentProjects,
    FinalApprovalWorkflow approvals,
    TimeProvider timeProvider)
{
    public async Task<PublicationWriteResult?> CreateAsync(
        Guid contentProjectId,
        string platform,
        string url,
        string videoId,
        DateTimeOffset publishedAt,
        string? notes,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        var normalizedPlatform = Normalize(
            () => PublicationRules.NormalizePlatform(platform),
            "publication_platform_invalid");
        var normalizedVideoId = Normalize(
            () => PublicationRules.NormalizeVideoId(videoId),
            "publication_video_id_invalid");
        var normalizedUrl = Normalize(
            () => PublicationRules.NormalizeVideoUrl(url, normalizedPlatform),
            "publication_url_invalid");
        var normalizedNotes = Normalize(
            () => PublicationRules.OptionalNotes(notes, nameof(notes)),
            "publication_invalid_request");

        if (publishedAt == default)
        {
            throw Error(
                "publication_invalid_request",
                "A non-default publishedAt timestamp is required.");
        }

        var project = await contentProjects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var existing = await publications.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        if (existing is not null)
        {
            // The record is a historical fact. Repeating the identical request is
            // idempotent even if the approval has since been invalidated.
            return existing.Matches(
                normalizedPlatform,
                normalizedUrl,
                normalizedVideoId,
                publishedAt,
                normalizedNotes)
                ? new PublicationWriteResult(false, ToSnapshot(existing))
                : throw Error(
                    "publication_conflict",
                    "This content project already has a different publication record.");
        }

        var status = await approvals.EvaluateAsync(contentProjectId, cancellationToken);
        if (status is null)
        {
            return null;
        }

        if (!status.IsApproved || status.Approval is null)
        {
            throw Error(
                "publication_approval_required",
                status.IsChangesRequested
                    ? "The A3 review requested changes; update the production and approve a new render before recording a publication."
                    : $"A valid final approval is required before recording a publication ({status.Reason}).");
        }

        if (!string.Equals(
            status.Approval.Destination,
            normalizedPlatform,
            StringComparison.Ordinal))
        {
            throw Error(
                "publication_destination_mismatch",
                $"The final approval covers destination '{status.Approval.Destination}', not '{normalizedPlatform}'.");
        }

        var record = PublicationRecord.Create(
            contentProjectId,
            status.Approval.Id,
            status.Approval.Revision,
            status.Approval.ArtifactContentHash,
            normalizedPlatform,
            normalizedUrl,
            normalizedVideoId,
            publishedAt,
            normalizedNotes,
            timeProvider.GetUtcNow());
        publications.Add(record);
        await publications.SaveChangesAsync(cancellationToken);
        return new PublicationWriteResult(true, ToSnapshot(record));
    }

    public async Task<PublicationSnapshot?> FindAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        var record = await publications.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        return record is null ? null : ToSnapshot(record);
    }

    internal static PublicationSnapshot ToSnapshot(PublicationRecord record) =>
        new(
            record.Id,
            record.ContentProjectId,
            record.FinalApprovalId,
            record.FinalApprovalRevision,
            record.ArtifactContentHash,
            record.Platform,
            record.Url,
            record.VideoId,
            record.PublishedAt,
            record.Notes,
            record.CreatedAt);

    private static T Normalize<T>(Func<T> normalize, string errorCode)
    {
        try
        {
            return normalize();
        }
        catch (ArgumentException exception)
        {
            throw new PublicationException(errorCode, exception.Message, exception);
        }
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }

    private static PublicationException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
