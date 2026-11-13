using System.Collections.Concurrent;
using AIStudio.Application.Research;

namespace AIStudio.Infrastructure.Research;

public sealed class InMemoryTrendCacheStore : ITrendCacheStore
{
    private readonly ConcurrentDictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<TrendSignal>?> GetAsync(
        string cacheKey,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (entries.TryGetValue(cacheKey, out var entry) && entry.ExpiresAt > utcNow)
        {
            return Task.FromResult<IReadOnlyList<TrendSignal>?>(entry.Signals);
        }

        entries.TryRemove(cacheKey, out _);
        return Task.FromResult<IReadOnlyList<TrendSignal>?>(null);
    }

    public Task SetAsync(
        string cacheKey,
        IReadOnlyList<TrendSignal> signals,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        entries[cacheKey] = new CacheEntry(signals.ToArray(), expiresAt);
        return Task.CompletedTask;
    }

    private sealed record CacheEntry(IReadOnlyList<TrendSignal> Signals, DateTimeOffset ExpiresAt);
}
