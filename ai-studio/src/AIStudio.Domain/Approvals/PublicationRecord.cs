using AIStudio.Domain.Content;

namespace AIStudio.Domain.Approvals;

/// <summary>
/// One manual publication record per ContentProject. This records a human upload the
/// operator already performed; it never uploads, authenticates, or calls a platform.
/// The record is bound to the exact final approval revision and artifact hash that
/// authorized it.
/// </summary>
public sealed class PublicationRecord
{
    public const int MaxPlatformLength = 32;
    public const int MaxUrlLength = 2048;
    public const int MaxVideoIdLength = 64;
    public const int MaxNotesLength = 2000;

    private PublicationRecord()
    {
    }

    private PublicationRecord(
        Guid id,
        Guid contentProjectId,
        Guid finalApprovalId,
        int finalApprovalRevision,
        string artifactContentHash,
        string platform,
        string url,
        string videoId,
        DateTimeOffset publishedAt,
        string? notes,
        DateTimeOffset createdAt)
    {
        Id = id;
        ContentProjectId = contentProjectId;
        FinalApprovalId = finalApprovalId;
        FinalApprovalRevision = finalApprovalRevision;
        ArtifactContentHash = artifactContentHash;
        Platform = platform;
        Url = url;
        VideoId = videoId;
        PublishedAt = publishedAt;
        Notes = notes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ContentProjectId { get; private set; }

    public ContentProject ContentProject { get; private set; } = null!;

    public Guid FinalApprovalId { get; private set; }

    public FinalApproval FinalApproval { get; private set; } = null!;

    public int FinalApprovalRevision { get; private set; }

    public string ArtifactContentHash { get; private set; } = string.Empty;

    public string Platform { get; private set; } = string.Empty;

    public string Url { get; private set; } = string.Empty;

    public string VideoId { get; private set; } = string.Empty;

    public DateTimeOffset PublishedAt { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PublicationRecord Create(
        Guid contentProjectId,
        Guid finalApprovalId,
        int finalApprovalRevision,
        string artifactContentHash,
        string platform,
        string url,
        string videoId,
        DateTimeOffset publishedAt,
        string? notes,
        DateTimeOffset utcNow,
        Guid? id = null)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(finalApprovalId, nameof(finalApprovalId));

        if (finalApprovalRevision < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(finalApprovalRevision),
                "Final approval revision must be at least one.");
        }

        var recordId = id ?? Guid.NewGuid();
        RequireIdentifier(recordId, nameof(id));

        var normalizedPlatform = PublicationRules.NormalizePlatform(platform);
        var normalizedVideoId = PublicationRules.NormalizeVideoId(videoId);
        var normalizedUrl = PublicationRules.NormalizeVideoUrl(url, normalizedPlatform);

        return new PublicationRecord(
            recordId,
            contentProjectId,
            finalApprovalId,
            finalApprovalRevision,
            PublicationRules.RequireHash(artifactContentHash, nameof(artifactContentHash)),
            normalizedPlatform,
            normalizedUrl,
            normalizedVideoId,
            publishedAt.ToUniversalTime(),
            PublicationRules.OptionalNotes(notes, nameof(notes)),
            utcNow.ToUniversalTime());
    }

    /// <summary>
    /// True when a repeated request describes the exact same publication, which makes
    /// the create call idempotent instead of inserting a duplicate record.
    /// </summary>
    public bool Matches(
        string platform,
        string url,
        string videoId,
        DateTimeOffset publishedAt,
        string? notes) =>
        string.Equals(Platform, platform, StringComparison.Ordinal)
        && string.Equals(Url, url, StringComparison.Ordinal)
        && string.Equals(VideoId, videoId, StringComparison.Ordinal)
        && PublishedAt.Equals(publishedAt.ToUniversalTime())
        && string.Equals(Notes, notes, StringComparison.Ordinal);

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }
}
