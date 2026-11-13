namespace AIStudio.Application.Research;

public interface IResearchRepository
{
    Task SavePackageAsync(ResearchPackage package, CancellationToken cancellationToken);

    Task<ResearchPackage?> FindPackageByIdAsync(Guid packageId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResearchPackageSummary>> ListPackagesAsync(int take, CancellationToken cancellationToken);

    Task<ProjectSourcesAndClaims?> GetProjectSourcesAndClaimsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    Task<PromotedProjectResult?> PromoteCandidateAsync(
        Guid packageId,
        int candidateIndex,
        string projectTitle,
        string projectBrief,
        IReadOnlyList<Guid> sourceIds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListRecentProjectTitlesAsync(int take, CancellationToken cancellationToken);
}

public sealed record ResearchPackageSummary(
    Guid Id,
    string QueryTopic,
    string RegionCode,
    string ProviderUsed,
    int SourceCount,
    int ClaimCount,
    int CandidateCount,
    bool DegradedFallback,
    DateTimeOffset CreatedAt);

public sealed record ProjectSourcesAndClaims(
    Guid ContentProjectId,
    Guid? SourceResearchPackageId,
    IReadOnlyList<ResearchSource> Sources,
    IReadOnlyList<EvidenceClaim> Claims);

public sealed record PromotedProjectResult(
    Guid ContentProjectId,
    string Title,
    Guid ResearchPackageId,
    int CandidateIndex,
    int LinkedSourceCount);
