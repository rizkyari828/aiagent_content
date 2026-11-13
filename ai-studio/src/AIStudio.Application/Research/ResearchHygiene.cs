using System.Security.Cryptography;
using System.Text;

namespace AIStudio.Application.Research;

public static class UrlCanonicalizer
{
    private static readonly HashSet<string> TrackingQueryParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid",
        "gclid",
        "si",
        "feature",
        "ref",
        "igshid",
        "mc_cid",
        "mc_eid"
    };

    public static string? TryCanonicalize(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if ((builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80)
            || (builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443))
        {
            builder.Port = -1;
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            var kept = new List<string>();
            var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var eqIndex = pair.IndexOf('=');
                var key = eqIndex >= 0 ? pair[..eqIndex] : pair;
                if (IsTrackingParam(key))
                {
                    continue;
                }

                kept.Add(pair);
            }

            builder.Query = string.Join('&', kept);
        }

        var result = builder.Uri.AbsoluteUri;
        if (result.EndsWith('/') && builder.Path == "/")
        {
            result = result[..^1];
        }

        return result;
    }

    private static bool IsTrackingParam(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        return key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || TrackingQueryParams.Contains(key);
    }
}

public static class ContentHashHelper
{
    public static string ComputeSnippetHash(string? title, string? summary)
    {
        var normalized = NormalizeText($"{title ?? string.Empty}\n{summary ?? string.Empty}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ComputeKeyHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeText(string value)
    {
        var sb = new StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
            }
        }

        return sb.ToString().TrimEnd();
    }
}

public static class FreshnessClassifier
{
    public static string Classify(DateTimeOffset? publishedAt, DateTimeOffset utcNow)
    {
        if (!publishedAt.HasValue)
        {
            return FreshnessTiers.Current;
        }

        var age = utcNow - publishedAt.Value.ToUniversalTime();
        if (age <= TimeSpan.FromHours(72))
        {
            return FreshnessTiers.Breaking;
        }

        if (age <= TimeSpan.FromDays(30))
        {
            return FreshnessTiers.Current;
        }

        return FreshnessTiers.Evergreen;
    }
}

public static class TrendMetricsCalculator
{
    public static TrendMetrics Compute(
        long? viewCount,
        long? likeCount,
        long? commentCount,
        DateTimeOffset? publishedAt,
        DateTimeOffset retrievedAt,
        long? channelMedianRecentViews = null)
    {
        double? viewsPerHour = null;
        if (viewCount.HasValue && publishedAt.HasValue)
        {
            var hours = Math.Max((retrievedAt - publishedAt.Value).TotalHours, 1.0);
            viewsPerHour = Math.Round(viewCount.Value / hours, 2);
        }

        double? outlierRatio = null;
        if (viewCount.HasValue)
        {
            var baseline = Math.Max(channelMedianRecentViews ?? 1_000L, 1_000L);
            outlierRatio = Math.Round((double)viewCount.Value / baseline, 2);
        }

        double? engagementRate = null;
        if (viewCount.HasValue && (likeCount.HasValue || commentCount.HasValue))
        {
            var engagements = (likeCount ?? 0L) + (commentCount ?? 0L);
            engagementRate = Math.Round((double)engagements / Math.Max(viewCount.Value, 1L), 4);
        }

        return new TrendMetrics(
            viewCount,
            likeCount,
            commentCount,
            viewsPerHour,
            outlierRatio,
            engagementRate);
    }
}

public static class CitationPolicy
{
    public static IReadOnlyList<EvidenceClaim> NormalizeClaims(
        IReadOnlyList<EvidenceClaim> claims,
        IReadOnlyDictionary<Guid, ResearchSource> sourcesById)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(sourcesById);

        var normalized = new List<EvidenceClaim>(claims.Count);
        foreach (var claim in claims)
        {
            var kind = ClaimKinds.All.Contains(claim.ClaimKind)
                ? claim.ClaimKind
                : ClaimKinds.SynthesisHypothesis;

            if (string.Equals(kind, ClaimKinds.VerifiedStat, StringComparison.Ordinal))
            {
                var hasValidSource = claim.SourceId.HasValue
                    && sourcesById.TryGetValue(claim.SourceId.Value, out var source)
                    && UrlCanonicalizer.TryCanonicalize(source.Url) is not null;

                if (!hasValidSource)
                {
                    var note = string.IsNullOrWhiteSpace(claim.ConfidenceNote)
                        ? "Downgraded to synthesis_hypothesis: missing verified source URL."
                        : $"{claim.ConfidenceNote} (Downgraded: missing verified source URL.)";

                    normalized.Add(claim with
                    {
                        SourceId = null,
                        ClaimKind = ClaimKinds.SynthesisHypothesis,
                        ConfidenceNote = note
                    });
                    continue;
                }
            }

            normalized.Add(claim with { ClaimKind = kind });
        }

        return normalized;
    }
}

public static class ResearchTokenDiet
{
    public static IReadOnlyList<TrendSignal> TrimAndDeduplicate(
        IEnumerable<TrendSignal> signals,
        int maxSignals = 12,
        int maxSummaryChars = 1_200,
        int maxCommentsPerSignal = 10)
    {
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<TrendSignal>();

        var ordered = signals
            .OrderByDescending(s => s.Metrics.OutlierRatio ?? 0.0)
            .ThenByDescending(s => s.Metrics.ViewsPerHour ?? 0.0)
            .ThenByDescending(s => s.RetrievedAt);

        foreach (var signal in ordered)
        {
            var canonicalUrl = UrlCanonicalizer.TryCanonicalize(signal.Url) ?? signal.Url.Trim();
            if (!string.IsNullOrWhiteSpace(canonicalUrl) && !seenUrls.Add(canonicalUrl))
            {
                continue;
            }

            var hash = string.IsNullOrWhiteSpace(signal.ContentHash)
                ? ContentHashHelper.ComputeSnippetHash(signal.Title, signal.RawSummary)
                : signal.ContentHash;

            if (!seenHashes.Add(hash))
            {
                continue;
            }

            var trimmedSummary = signal.RawSummary.Length > maxSummaryChars
                ? signal.RawSummary[..maxSummaryChars].TrimEnd() + "..."
                : signal.RawSummary;

            var trimmedComments = signal.TopComments?
                .Take(maxCommentsPerSignal)
                .Select(c => c with
                {
                    Text = c.Text.Length > 280 ? c.Text[..280].TrimEnd() + "..." : c.Text
                })
                .ToArray();

            unique.Add(signal with
            {
                Url = canonicalUrl,
                ContentHash = hash,
                RawSummary = trimmedSummary,
                TopComments = trimmedComments
            });

            if (unique.Count >= maxSignals)
            {
                break;
            }
        }

        return unique;
    }
}
