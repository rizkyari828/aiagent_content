using System.Xml;
using System.Xml.Linq;
using AIStudio.Application.Research;

namespace AIStudio.Infrastructure.Research;

internal static class RssXmlFeedParser
{
    public static IReadOnlyList<TrendSignal> Parse(
        string xml,
        string sourceType,
        DateTimeOffset retrievedAt,
        string? defaultAuthor = null,
        int maxItems = 15)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        XDocument doc;
        using (var stringReader = new StringReader(xml))
        using (var xmlReader = XmlReader.Create(stringReader, settings))
        {
            doc = XDocument.Load(xmlReader);
        }

        var root = doc.Root;
        if (root is null)
        {
            return [];
        }

        var items = root.Descendants()
            .Where(e => e.Name.LocalName is "item" or "entry")
            .Take(maxItems);

        var signals = new List<TrendSignal>();
        foreach (var item in items)
        {
            var title = item.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var linkElement = item.Elements().FirstOrDefault(e => e.Name.LocalName == "link");
            var rawLink = linkElement?.Attribute("href")?.Value ?? linkElement?.Value;
            var canonicalUrl = UrlCanonicalizer.TryCanonicalize(rawLink);
            if (canonicalUrl is null)
            {
                continue;
            }

            var summaryRaw = item.Elements()
                .FirstOrDefault(e => e.Name.LocalName is "description" or "summary" or "content")
                ?.Value ?? string.Empty;
            var summary = StripHtmlTags(summaryRaw).Trim();
            if (summary.Length > 1_200)
            {
                summary = summary[..1_200].TrimEnd() + "...";
            }

            var pubDateRaw = item.Elements()
                .FirstOrDefault(e => e.Name.LocalName is "pubDate" or "published" or "updated")
                ?.Value;
            DateTimeOffset? publishedAt = DateTimeOffset.TryParse(pubDateRaw, out var parsedDate)
                ? parsedDate.ToUniversalTime()
                : null;

            var author = item.Elements()
                .FirstOrDefault(e => e.Name.LocalName is "author" or "creator")
                ?.Value.Trim();
            if (string.IsNullOrWhiteSpace(author))
            {
                author = defaultAuthor;
            }

            var hash = ContentHashHelper.ComputeSnippetHash(title, summary);
            signals.Add(new TrendSignal(
                sourceType,
                hash[..16],
                title,
                canonicalUrl,
                author,
                publishedAt,
                retrievedAt,
                new TrendMetrics(),
                string.IsNullOrWhiteSpace(summary) ? title : summary,
                FreshnessClassifier.Classify(publishedAt, retrievedAt),
                hash));
        }

        return signals;
    }

    private static string StripHtmlTags(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var array = new char[input.Length];
        var arrayIndex = 0;
        var inside = false;
        foreach (var ch in input)
        {
            if (ch == '<')
            {
                inside = true;
                continue;
            }

            if (ch == '>')
            {
                inside = false;
                array[arrayIndex++] = ' ';
                continue;
            }

            if (!inside)
            {
                array[arrayIndex++] = ch;
            }
        }

        return new string(array, 0, arrayIndex);
    }
}
