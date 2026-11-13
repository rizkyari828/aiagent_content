using System.Text.Json;
using AIStudio.Application.Research;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Research;

public sealed class YouTubeDataApiTrendCollector(
    HttpClient httpClient,
    IOptions<YouTubeOptions> options,
    TimeProvider timeProvider) : ITrendCollector
{
    public string SourceType => ResearchSourceTypes.YouTube;

    public bool IsEnabled =>
        options.Value.Enabled && !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<IReadOnlyList<TrendSignal>> CollectAsync(
        TrendQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!IsEnabled)
        {
            return [];
        }

        var settings = options.Value;
        var baseUrl = settings.BaseUrl.TrimEnd('/');
        var apiKey = Uri.EscapeDataString(settings.ApiKey.Trim());
        var region = string.IsNullOrWhiteSpace(query.RegionCode)
            ? "ID"
            : Uri.EscapeDataString(query.RegionCode.Trim().ToUpperInvariant());
        var maxResults = Math.Clamp(query.MaxSignals, 1, 15);
        var retrievedAt = timeProvider.GetUtcNow();

        var videoIds = await FetchCandidateVideoIdsAsync(
            baseUrl,
            apiKey,
            region,
            query.Topic,
            maxResults,
            cancellationToken);

        if (videoIds.Count == 0)
        {
            return [];
        }

        var signals = await FetchVideoDetailsAsync(
            baseUrl,
            apiKey,
            videoIds,
            retrievedAt,
            cancellationToken);

        var ordered = signals
            .OrderByDescending(s => s.Metrics.OutlierRatio ?? 0.0)
            .ThenByDescending(s => s.Metrics.ViewsPerHour ?? 0.0)
            .ToList();

        var commentTopCount = Math.Clamp(settings.CommentMiningTopVideos, 0, ordered.Count);
        for (var i = 0; i < commentTopCount; i++)
        {
            var comments = await FetchTopCommentsAsync(
                baseUrl,
                apiKey,
                ordered[i].ExternalId,
                Math.Clamp(settings.CommentsPerVideo, 1, 30),
                cancellationToken);

            if (comments.Count > 0)
            {
                ordered[i] = ordered[i] with { TopComments = comments };
            }
        }

        return ordered;
    }

    private async Task<IReadOnlyList<string>> FetchCandidateVideoIdsAsync(
        string baseUrl,
        string apiKey,
        string region,
        string topic,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var escapedTopic = Uri.EscapeDataString(topic.Trim());
        var searchUrl =
            $"{baseUrl}/search?part=snippet&type=video&order=viewCount&regionCode={region}&maxResults={maxResults}&q={escapedTopic}&key={apiKey}";

        using var response = await httpClient.GetAsync(searchUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idObj)
                && idObj.TryGetProperty("videoId", out var vid)
                && vid.ValueKind == JsonValueKind.String)
            {
                var id = vid.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private async Task<List<TrendSignal>> FetchVideoDetailsAsync(
        string baseUrl,
        string apiKey,
        IReadOnlyList<string> videoIds,
        DateTimeOffset retrievedAt,
        CancellationToken cancellationToken)
    {
        var joinedIds = Uri.EscapeDataString(string.Join(',', videoIds));
        var url = $"{baseUrl}/videos?part=snippet,statistics&id={joinedIds}&key={apiKey}";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        if (!doc.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var signals = new List<TrendSignal>();
        foreach (var item in items.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!item.TryGetProperty("snippet", out var snippet))
            {
                continue;
            }

            var title = snippet.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var channelTitle = snippet.TryGetProperty("channelTitle", out var cEl) ? cEl.GetString() : null;
            var description = snippet.TryGetProperty("description", out var dEl) ? dEl.GetString() ?? string.Empty : string.Empty;
            DateTimeOffset? publishedAt = snippet.TryGetProperty("publishedAt", out var pEl)
                && DateTimeOffset.TryParse(pEl.GetString(), out var parsedDate)
                ? parsedDate.ToUniversalTime()
                : null;

            long? viewCount = null;
            long? likeCount = null;
            long? commentCount = null;
            if (item.TryGetProperty("statistics", out var stats))
            {
                viewCount = TryParseLong(stats, "viewCount");
                likeCount = TryParseLong(stats, "likeCount");
                commentCount = TryParseLong(stats, "commentCount");
            }

            var metrics = TrendMetricsCalculator.Compute(
                viewCount,
                likeCount,
                commentCount,
                publishedAt,
                retrievedAt);

            var canonicalUrl = $"https://www.youtube.com/watch?v={id}";
            var summary = description.Length > 1_200 ? description[..1_200].TrimEnd() + "..." : description;
            var hash = ContentHashHelper.ComputeSnippetHash(title, summary);

            signals.Add(new TrendSignal(
                ResearchSourceTypes.YouTube,
                id,
                title,
                canonicalUrl,
                channelTitle,
                publishedAt,
                retrievedAt,
                metrics,
                string.IsNullOrWhiteSpace(summary) ? title : summary,
                FreshnessClassifier.Classify(publishedAt, retrievedAt),
                hash));
        }

        return signals;
    }

    private async Task<IReadOnlyList<CommentInsight>> FetchTopCommentsAsync(
        string baseUrl,
        string apiKey,
        string videoId,
        int maxComments,
        CancellationToken cancellationToken)
    {
        var url =
            $"{baseUrl}/commentThreads?part=snippet&videoId={Uri.EscapeDataString(videoId)}&order=relevance&maxResults={maxComments}&key={apiKey}";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var comments = new List<CommentInsight>();
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("snippet", out var threadSnippet)
                    || !threadSnippet.TryGetProperty("topLevelComment", out var topComment)
                    || !topComment.TryGetProperty("snippet", out var commentSnippet))
                {
                    continue;
                }

                var text = commentSnippet.TryGetProperty("textDisplay", out var textEl)
                    ? textEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var likes = commentSnippet.TryGetProperty("likeCount", out var likesEl)
                    && likesEl.TryGetInt32(out var l)
                    ? l
                    : 0;

                var trimmed = text.Trim();
                if (trimmed.Length > 280)
                {
                    trimmed = trimmed[..280].TrimEnd() + "...";
                }

                comments.Add(new CommentInsight(trimmed, likes));
            }

            return comments;
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

    private static long? TryParseLong(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var num))
        {
            return num;
        }

        if (el.ValueKind == JsonValueKind.String
            && long.TryParse(el.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
