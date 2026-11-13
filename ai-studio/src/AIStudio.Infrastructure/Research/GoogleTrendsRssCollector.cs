using AIStudio.Application.Research;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Research;

public sealed class GoogleTrendsRssCollector(
    HttpClient httpClient,
    IOptions<ResearchOptions> options,
    TimeProvider timeProvider) : ITrendCollector
{
    public string SourceType => ResearchSourceTypes.GoogleTrends;

    public bool IsEnabled => options.Value.GoogleTrendsEnabled;

    public async Task<IReadOnlyList<TrendSignal>> CollectAsync(
        TrendQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!IsEnabled)
        {
            return [];
        }

        var region = string.IsNullOrWhiteSpace(query.RegionCode)
            ? "ID"
            : Uri.EscapeDataString(query.RegionCode.Trim().ToUpperInvariant());

        var baseUrl = options.Value.GoogleTrendsBaseUrl.TrimEnd('?');
        var requestUrl = $"{baseUrl}?geo={region}";

        using var response = await httpClient.GetAsync(requestUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var retrievedAt = timeProvider.GetUtcNow();
        return RssXmlFeedParser.Parse(
            xml,
            ResearchSourceTypes.GoogleTrends,
            retrievedAt,
            defaultAuthor: $"Google Trends ({region})",
            maxItems: Math.Clamp(query.MaxSignals, 1, 20));
    }
}
