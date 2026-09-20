using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AIStudio.Application.Rendering.Narration;

namespace AIStudio.Application.Rendering;

/// <summary>
/// One scene's narration text placed on the production timeline, used to derive
/// phrase-level subtitles from the same source spoken by TTS.
/// </summary>
public sealed record NarrationSubtitleScene(
    int SceneIndex,
    string Text,
    double StartSeconds,
    double DurationSeconds);

/// <summary>
/// Serializes narration-aware subtitle cues against the same derived scene timing
/// used for the video. It only builds derived bytes: the canonical/original
/// subtitle asset is never modified.
/// </summary>
public static class SubtitleTimeline
{
    private static readonly Regex PhraseBoundary = new(
        @"(?<=[.!?;:])\s+|\n+",
        RegexOptions.Compiled);

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

    /// <summary>
    /// Builds phrase-level cues from each scene's approved narration text. Cues are
    /// allocated within the scene's own speech window (weighted by phrase length),
    /// so no cue ever crosses a scene boundary.
    /// </summary>
    public static string BuildFromNarration(IReadOnlyList<NarrationSubtitleScene> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        var builder = new StringBuilder();
        var cueIndex = 0;

        foreach (var scene in scenes)
        {
            if (scene.DurationSeconds <= 0)
            {
                continue;
            }

            var phrases = SplitPhrases(scene.Text);
            if (phrases.Count == 0)
            {
                continue;
            }

            var totalCharacters = phrases.Sum(phrase => phrase.Length);
            var windowEnd = scene.StartSeconds + scene.DurationSeconds;
            var cursor = scene.StartSeconds;

            for (var index = 0; index < phrases.Count; index++)
            {
                var share = totalCharacters > 0
                    ? (double)phrases[index].Length / totalCharacters
                    : 1d / phrases.Count;
                var duration = scene.DurationSeconds * share;
                var cueEnd = index == phrases.Count - 1
                    ? windowEnd
                    : Math.Min(windowEnd, cursor + duration);

                if (cueEnd - cursor <= 0.05)
                {
                    continue;
                }

                cueIndex++;
                builder.AppendLine(cueIndex.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine($"{Timestamp(cursor)} --> {Timestamp(cueEnd)}");
                builder.AppendLine(WrapPhrase(phrases[index]));
                builder.AppendLine();
                cursor = cueEnd;
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> SplitPhrases(string text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        var phrases = new List<string>();

        foreach (var raw in PhraseBoundary.Split(normalized))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (var phrase in SplitLongPhrase(trimmed))
            {
                if (phrase.Length > 0)
                {
                    phrases.Add(phrase);
                }
            }
        }

        return phrases;
    }

    private static IEnumerable<string> SplitLongPhrase(string phrase)
    {
        var limit = NarrativeTiming.MaxSubtitleLineCharacters * NarrativeTiming.MaxSubtitleLines;
        if (phrase.Length <= limit)
        {
            yield return phrase;
            yield break;
        }

        var current = new StringBuilder();
        foreach (var commaPart in phrase.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = commaPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (current.Length > 0 && current.Length + part.Length + 2 > limit)
            {
                yield return current.ToString().Trim();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(", ");
            }

            current.Append(part);
        }

        if (current.Length > 0)
        {
            yield return current.ToString().Trim();
        }
    }

    private static string WrapPhrase(string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0
                && current.Length + 1 + word.Length > NarrativeTiming.MaxSubtitleLineCharacters)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return string.Join('\n', lines);
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
