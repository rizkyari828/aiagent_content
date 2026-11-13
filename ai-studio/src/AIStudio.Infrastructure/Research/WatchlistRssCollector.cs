using AIStudio.Application.Research;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Research;

public sealed class WatchlistRssCollector(
    HttpClient httpClient,
    IResearchWatchlistProvider watchlistProvider,
    IOptions<ResearchOptions> options,
    TimeProvider timeProvider) : ITrendCollector
{
    public string SourceType => ResearchSourceTypes.WatchlistRss;

    public bool IsEnabled => watchlistProvider.GetFeeds().Count > 0;

    public async Task<IReadOnlyList<TrendSignal>> CollectAsync(
        TrendQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!query.IncludeWatchlist || !IsEnabled)
        {
            return [];
        }

        var feeds = watchlistProvider.GetFeeds();
        var retrievedAt = timeProvider.GetUtcNow();
        var results = new List<TrendSignal>();

        foreach (var feed in feeds.Take(10))
        {
            if (UrlCanonicalizer.TryCanonicalize(feed.Url) is null)
            {
                continue;
            }

            try
            {
                using var response = await httpClient.GetAsync(feed.Url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = RssXmlFeedParser.Parse(
                    xml,
                    ResearchSourceTypes.WatchlistRss,
                    retrievedAt,
                    defaultAuthor: string.IsNullOrWhiteSpace(feed.Title) ? feed.Category : feed.Title,
                    maxItems: 5);
                results.AddRange(parsed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Keep remaining watchlist feeds resilient
            }
        }

        _ = options.Value;
        return results;
    }
}

public sealed class OptionsResearchWatchlistProvider(
    IOptions<ResearchOptions> options) : IResearchWatchlistProvider
{
    public IReadOnlyList<WatchlistFeedDescriptor> GetFeeds() =>
        options.Value.WatchlistFeeds
            .Where(f => UrlCanonicalizer.TryCanonicalize(f.Url) is not null)
            .Select((f, idx) => new WatchlistFeedDescriptor(
                string.IsNullOrWhiteSpace(f.Id) ? $"feed-{idx + 1}" : f.Id.Trim(),
                string.IsNullOrWhiteSpace(f.Title) ? f.Url : f.Title.Trim(),
                UrlCanonicalizer.TryCanonicalize(f.Url)!,
                string.IsNullOrWhiteSpace(f.Category) ? "general" : f.Category.Trim()))
            .ToArray();
}
