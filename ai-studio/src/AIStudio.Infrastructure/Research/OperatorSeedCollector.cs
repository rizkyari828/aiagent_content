using AIStudio.Application.Research;

namespace AIStudio.Infrastructure.Research;

public sealed class OperatorSeedCollector(TimeProvider timeProvider) : ITrendCollector
{
    public string SourceType => ResearchSourceTypes.OperatorSeed;

    public bool IsEnabled => true;

    public Task<IReadOnlyList<TrendSignal>> CollectAsync(
        TrendQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.OperatorSeedUrls is not { Count: > 0 })
        {
            return Task.FromResult<IReadOnlyList<TrendSignal>>([]);
        }

        var utcNow = timeProvider.GetUtcNow();
        var results = new List<TrendSignal>();

        foreach (var rawEntry in query.OperatorSeedUrls)
        {
            if (string.IsNullOrWhiteSpace(rawEntry))
            {
                continue;
            }

            var trimmed = rawEntry.Trim();
            string title;
            string? url;

            var pipeIndex = trimmed.IndexOf('|');
            if (pipeIndex > 0)
            {
                title = trimmed[..pipeIndex].Trim();
                url = UrlCanonicalizer.TryCanonicalize(trimmed[(pipeIndex + 1)..]);
            }
            else
            {
                url = UrlCanonicalizer.TryCanonicalize(trimmed);
                title = url is not null ? BuildTitleFromUrl(url) : trimmed;
            }

            if (url is null)
            {
                continue;
            }

            var hash = ContentHashHelper.ComputeSnippetHash(title, trimmed);
            results.Add(new TrendSignal(
                ResearchSourceTypes.OperatorSeed,
                hash[..16],
                title,
                url,
                "operator-seed",
                utcNow,
                utcNow,
                new TrendMetrics(),
                $"Operator-provided FYP/short-form seed for '{query.Topic}': {title}",
                FreshnessTiers.Breaking,
                hash));
        }

        return Task.FromResult<IReadOnlyList<TrendSignal>>(results);
    }

    private static string BuildTitleFromUrl(string canonicalUrl)
    {
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var uri))
        {
            return canonicalUrl;
        }

        var path = uri.AbsolutePath.Trim('/');
        return string.IsNullOrWhiteSpace(path)
            ? uri.Host
            : $"{uri.Host}/{path}";
    }
}
