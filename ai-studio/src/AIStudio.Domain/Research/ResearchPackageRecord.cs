namespace AIStudio.Domain.Research;

public sealed class ResearchPackageRecord
{
    public const int MaxTopicLength = 500;
    public const int MaxRegionLength = 16;
    public const int MaxProviderLength = 128;

    private ResearchPackageRecord()
    {
    }

    private ResearchPackageRecord(
        Guid id,
        string queryTopic,
        string regionCode,
        string providerUsed,
        string candidatesJson,
        string? pacingBlueprintJson,
        string? warningsJson,
        bool degradedFallback,
        DateTimeOffset createdAt)
    {
        Id = id;
        QueryTopic = queryTopic;
        RegionCode = regionCode;
        ProviderUsed = providerUsed;
        CandidatesJson = candidatesJson;
        PacingBlueprintJson = pacingBlueprintJson;
        WarningsJson = warningsJson;
        DegradedFallback = degradedFallback;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string QueryTopic { get; private set; } = string.Empty;

    public string RegionCode { get; private set; } = "ID";

    public string ProviderUsed { get; private set; } = string.Empty;

    public string CandidatesJson { get; private set; } = "[]";

    public string? PacingBlueprintJson { get; private set; }

    public string? WarningsJson { get; private set; }

    public bool DegradedFallback { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ResearchPackageRecord Create(
        Guid id,
        string queryTopic,
        string regionCode,
        string providerUsed,
        string candidatesJson,
        string? pacingBlueprintJson,
        string? warningsJson,
        bool degradedFallback,
        DateTimeOffset utcNow)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Research package ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(queryTopic) || queryTopic.Trim().Length > MaxTopicLength)
        {
            throw new ArgumentException($"Query topic must be 1..{MaxTopicLength} chars.", nameof(queryTopic));
        }

        return new ResearchPackageRecord(
            id,
            queryTopic.Trim(),
            string.IsNullOrWhiteSpace(regionCode) ? "ID" : regionCode.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(providerUsed) ? "unknown" : providerUsed.Trim(),
            string.IsNullOrWhiteSpace(candidatesJson) ? "[]" : candidatesJson,
            pacingBlueprintJson,
            warningsJson,
            degradedFallback,
            utcNow.ToUniversalTime());
    }
}
