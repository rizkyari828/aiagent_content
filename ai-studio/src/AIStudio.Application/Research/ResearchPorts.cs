namespace AIStudio.Application.Research;

public interface ITrendCollector
{
    string SourceType { get; }

    bool IsEnabled { get; }

    Task<IReadOnlyList<TrendSignal>> CollectAsync(
        TrendQuery query,
        CancellationToken cancellationToken);
}

public interface IResearchSynthesizer
{
    Task<ResearchPackage> SynthesizeAsync(
        ResearchRequest request,
        CancellationToken cancellationToken);
}

public interface ITrendCacheStore
{
    Task<IReadOnlyList<TrendSignal>?> GetAsync(
        string cacheKey,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task SetAsync(
        string cacheKey,
        IReadOnlyList<TrendSignal> signals,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);
}

public interface IResearchWatchlistProvider
{
    IReadOnlyList<WatchlistFeedDescriptor> GetFeeds();
}
