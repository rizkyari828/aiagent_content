namespace AIStudio.Application.Research;

public sealed class TrendCollectionService(
    IEnumerable<ITrendCollector> collectors,
    ITrendCacheStore cacheStore,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromHours(8);

    public async Task<IReadOnlyList<TrendSignal>> CollectAsync(
        TrendQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Topic))
        {
            throw new ResearchException("research_topic_required", "Research topic is required.");
        }

        var utcNow = timeProvider.GetUtcNow();
        var cacheKey = BuildCacheKey(query, utcNow);

        var cached = await cacheStore.GetAsync(cacheKey, utcNow, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var tasks = collectors
            .Where(c => c.IsEnabled)
            .Select(c => CollectSafeAsync(c, query, cancellationToken));

        var batches = await Task.WhenAll(tasks);
        var merged = ResearchTokenDiet.TrimAndDeduplicate(
            batches.SelectMany(b => b),
            maxSignals: Math.Clamp(query.MaxSignals, 1, 25));

        if (merged.Count > 0)
        {
            await cacheStore.SetAsync(cacheKey, merged, utcNow.Add(DefaultCacheTtl), cancellationToken);
        }

        return merged;
    }

    private static async Task<IReadOnlyList<TrendSignal>> CollectSafeAsync(
        ITrendCollector collector,
        TrendQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            return await collector.CollectAsync(query, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    public static string BuildCacheKey(TrendQuery query, DateTimeOffset utcNow)
    {
        var region = string.IsNullOrWhiteSpace(query.RegionCode) ? "ID" : query.RegionCode.Trim().ToUpperInvariant();
        var topicNorm = query.Topic.Trim().ToLowerInvariant();
        var seeds = string.Join('|', (query.OperatorSeedUrls ?? []).Select(s => s.Trim().ToLowerInvariant()).OrderBy(s => s));
        var dateBucket = utcNow.ToUniversalTime().ToString("yyyy-MM-dd");
        return ContentHashHelper.ComputeKeyHash($"{topicNorm}::{region}::{query.IncludeWatchlist}::{seeds}::{dateBucket}");
    }
}
