using System.Text;
using System.Text.RegularExpressions;

namespace AIStudio.Application.Rendering.Narration;

/// <summary>
/// Deterministic narration-text normalization shared by speech synthesis and
/// subtitles, so both read exactly the same approved text. It strips quote marks
/// (VoxCPM2 produces degenerate, near-zero-length audio when a span is wrapped in
/// quotes) and collapses whitespace. Nothing semantic is added or invented.
/// </summary>
public static class NarrationText
{
    private static readonly char[] QuoteCharacters =
        ['\'', '"', '\u2018', '\u2019', '\u201C', '\u201D', '\u00AB', '\u00BB'];

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (Array.IndexOf(QuoteCharacters, character) < 0)
            {
                builder.Append(character);
            }
        }

        return Whitespace.Replace(builder.ToString(), " ").Trim();
    }
}
