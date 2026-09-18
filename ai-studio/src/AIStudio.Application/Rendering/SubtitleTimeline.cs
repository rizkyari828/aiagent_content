using System.Globalization;
using System.Text;

namespace AIStudio.Application.Rendering;

/// <summary>
/// Serializes narration-aware subtitle cues against the same derived scene timing
/// used for the video. It only builds derived bytes: the canonical/original
/// subtitle asset is never modified.
/// </summary>
public static class SubtitleTimeline
{
    public static string Build(
        IReadOnlyList<string> cueTexts,
        IReadOnlyList<double> durations)
    {
        ArgumentNullException.ThrowIfNull(cueTexts);
        ArgumentNullException.ThrowIfNull(durations);
        if (cueTexts.Count != durations.Count)
        {
            throw new ArgumentException(
                "Cue texts and durations must have the same length.");
        }

        var builder = new StringBuilder();
        var cursor = 0d;
        for (var index = 0; index < cueTexts.Count; index++)
        {
            builder.AppendLine((index + 1).ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(
                $"{Timestamp(cursor)} --> {Timestamp(cursor + durations[index])}");
            builder.AppendLine(cueTexts[index]);
            builder.AppendLine();
            cursor += durations[index];
        }

        return builder.ToString();
    }

    public static string Timestamp(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00},{span.Milliseconds:000}";
    }

    /// <summary>
    /// Reads the ordered display text of each cue from canonical SRT content, so a
    /// derived timeline can keep the operator's wording and only change boundaries.
    /// Returns an empty list when the content has no parsable cues.
    /// </summary>
    public static IReadOnlyList<string> ReadCueTexts(string srtContent)
    {
        var cues = new List<string>();
        var block = new List<string>();

        void Flush()
        {
            if (block.Count >= 3 && block[1].Contains("-->", StringComparison.Ordinal))
            {
                cues.Add(string.Join('\n', block.Skip(2)));
            }

            block.Clear();
        }

        foreach (var raw in (srtContent ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n'))
        {
            if (raw.Trim().Length == 0)
            {
                Flush();
                continue;
            }

            block.Add(raw);
        }

        Flush();
        return cues;
    }
}
