namespace AIStudio.Domain.Research;

public sealed class ResearchSourceRecord
{
    public const int MaxTitleLength = 500;
    public const int MaxUrlLength = 2_000;
    public const int MaxSourceTypeLength = 64;
    public const int MaxHashLength = 128;

    private ResearchSourceRecord()
    {
    }

    private ResearchSourceRecord(
        Guid id,
        Guid researchPackageId,
        Guid? contentProjectId,
        string title,
        string url,
        string sourceType,
        string snapshotHash,
        DateTimeOffset retrievedAt)
    {
        Id = id;
        ResearchPackageId = researchPackageId;
        ContentProjectId = contentProjectId;
        Title = title;
        Url = url;
        SourceType = sourceType;
        SnapshotHash = snapshotHash;
        RetrievedAt = retrievedAt;
    }

    public Guid Id { get; private set; }

    public Guid ResearchPackageId { get; private set; }

    public Guid? ContentProjectId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Url { get; private set; } = string.Empty;

    public string SourceType { get; private set; } = string.Empty;

    public string SnapshotHash { get; private set; } = string.Empty;

    public DateTimeOffset RetrievedAt { get; private set; }

    public static ResearchSourceRecord Create(
        Guid id,
        Guid researchPackageId,
        Guid? contentProjectId,
        string title,
        string url,
        string sourceType,
        string snapshotHash,
        DateTimeOffset retrievedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Source ID cannot be empty.", nameof(id));
        }

        if (researchPackageId == Guid.Empty)
        {
            throw new ArgumentException("Research package ID cannot be empty.", nameof(researchPackageId));
        }

        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? url : title.Trim();
        if (normalizedTitle.Length > MaxTitleLength)
        {
            normalizedTitle = normalizedTitle[..MaxTitleLength];
        }

        return new ResearchSourceRecord(
            id,
            researchPackageId,
            contentProjectId,
            normalizedTitle,
            url.Trim(),
            sourceType.Trim(),
            snapshotHash.Trim(),
            retrievedAt.ToUniversalTime());
    }

    public void AttachToProject(Guid contentProjectId)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException("Content project ID cannot be empty.", nameof(contentProjectId));
        }

        ContentProjectId = contentProjectId;
    }
}
