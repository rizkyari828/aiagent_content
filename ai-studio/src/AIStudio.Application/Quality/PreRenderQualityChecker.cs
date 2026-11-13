using System.Globalization;
using System.Text.RegularExpressions;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Scripts;
using AIStudio.Application.Subtitles;

namespace AIStudio.Application.Quality;

public static partial class PreRenderQualityChecker
{
    public const double DefaultWordsPerSecond = 2.3;
    public const double MaxDurationDeviationRatio = 0.35;
    public const double MaxSubtitleCps = 20.0;
    public const int MaxSubtitleLineChars = 42;
    public const int MaxSubtitleLinesPerCue = 2;

    [GeneratedRegex(@"\[(?:sfx|music|cut|pause|beat|bgm)\b[^\]]*\]|\((?:sfx|cut|beat|camera|zoom|pan)\b[^\)]*\)|https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex SpokenLeakPattern();

    [GeneratedRegex(@"^(\d{2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[,.](\d{3})")]
    private static partial Regex SrtTimecodePattern();

    public static PreRenderCheckReport Inspect(
        GenerateScriptResult? script,
        string? srtContent,
        double? targetDurationSeconds = null)
    {
        var issues = new List<PreRenderIssue>();
        int scriptWordCount = 0;
        double estimatedSpeechSeconds = 0;

        if (script is not null)
        {
            var spokenParts = new List<string> { script.OpeningHook };
            spokenParts.AddRange(script.Sections.Select(s => s.Narration));
            spokenParts.Add(script.Closing);

            var fullSpoken = string.Join(' ', spokenParts);
            scriptWordCount = CountWords(fullSpoken);
            estimatedSpeechSeconds = Math.Round(scriptWordCount / DefaultWordsPerSecond, 1);

            var hookWords = CountWords(script.OpeningHook);
            if (hookWords > 18)
            {
                issues.Add(new PreRenderIssue(
                    "warning",
                    "hook_length",
                    $"Opening hook is {hookWords} words (recommended <= 18 words for 3-second retention)."));
            }

            if (targetDurationSeconds is > 0)
            {
                var deviation = Math.Abs(estimatedSpeechSeconds - targetDurationSeconds.Value) / targetDurationSeconds.Value;
                if (deviation > MaxDurationDeviationRatio)
                {
                    issues.Add(new PreRenderIssue(
                        "warning",
                        "pacing_deviation",
                        $"Estimated narration (~{estimatedSpeechSeconds:F1}s, {scriptWordCount} words) deviates >35% from target {targetDurationSeconds.Value:F1}s."));
                }
            }

            foreach (var part in spokenParts)
            {
                var match = SpokenLeakPattern().Match(part);
                if (match.Success)
                {
                    issues.Add(new PreRenderIssue(
                        "error",
                        "spoken_leak",
                        $"Spoken narration contains non-spoken cue or URL '{match.Value}'."));
                }
            }
        }

        int cueCount = 0;
        if (!string.IsNullOrWhiteSpace(srtContent))
        {
            var srtIssues = InspectSrt(srtContent, out cueCount);
            issues.AddRange(srtIssues);
        }

        return new PreRenderCheckReport(
            Passed: issues.All(i => !string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            ScriptWordCount: scriptWordCount,
            EstimatedSpeechSeconds: estimatedSpeechSeconds,
            SubtitleCueCount: cueCount,
            Issues: issues);
    }

    public static IReadOnlyList<PreRenderIssue> InspectSrt(string srtContent, out int cueCount)
    {
        var issues = new List<PreRenderIssue>();
        cueCount = 0;
        if (string.IsNullOrWhiteSpace(srtContent))
        {
            return issues;
        }

        var blocks = srtContent
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                continue;
            }

            var timeLineIndex = SrtTimecodePattern().IsMatch(lines[0]) ? 0 : 1;
            if (timeLineIndex >= lines.Length)
            {
                continue;
            }

            var match = SrtTimecodePattern().Match(lines[timeLineIndex]);
            if (!match.Success)
            {
                continue;
            }

            cueCount++;
            var start = ParseTime(match, 1);
            var end = ParseTime(match, 5);
            var durationSec = Math.Max((end - start).TotalSeconds, 0.1);

            var textLines = lines.Skip(timeLineIndex + 1).ToArray();
            if (textLines.Length > MaxSubtitleLinesPerCue)
            {
                issues.Add(new PreRenderIssue(
                    "warning",
                    "srt_line_count",
                    $"Subtitle cue #{cueCount} has {textLines.Length} lines (max {MaxSubtitleLinesPerCue})."));
            }

            foreach (var line in textLines)
            {
                if (line.Length > MaxSubtitleLineChars)
                {
                    issues.Add(new PreRenderIssue(
                        "warning",
                        "srt_line_length",
                        $"Subtitle cue #{cueCount} line exceeds {MaxSubtitleLineChars} chars ({line.Length} chars)."));
                }
            }

            var charCount = textLines.Sum(l => l.Trim().Length);
            var cps = charCount / durationSec;
            if (cps > MaxSubtitleCps)
            {
                issues.Add(new PreRenderIssue(
                    "warning",
                    "srt_cps",
                    $"Subtitle cue #{cueCount} reading speed is {cps:F1} CPS (recommended <= {MaxSubtitleCps})."));
            }
        }

        return issues;
    }

    private static TimeSpan ParseTime(Match m, int groupOffset)
    {
        var h = int.Parse(m.Groups[groupOffset].Value, CultureInfo.InvariantCulture);
        var min = int.Parse(m.Groups[groupOffset + 1].Value, CultureInfo.InvariantCulture);
        var sec = int.Parse(m.Groups[groupOffset + 2].Value, CultureInfo.InvariantCulture);
        var ms = int.Parse(m.Groups[groupOffset + 3].Value, CultureInfo.InvariantCulture);
        return new TimeSpan(0, h, min, sec, ms);
    }

    private static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
}

public sealed class PreRenderQualityWorkflow(
    IContentProjectReader contentProjects,
    IScriptReviewRepository scripts,
    SubtitleWorkflow subtitleWorkflow)
{
    public async Task<PreRenderCheckReport?> CheckProjectAsync(
        Guid contentProjectId,
        double? targetDurationSeconds,
        CancellationToken cancellationToken)
    {
        var project = await contentProjects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        GenerateScriptResult? scriptResult = null;
        var reviewedScript = await scripts.FindByProjectIdAsync(contentProjectId, cancellationToken);
        if (reviewedScript is not null)
        {
            try
            {
                scriptResult = GenerateScriptResult.Deserialize(reviewedScript.Content);
            }
            catch
            {
                // Ignore invalid script here; handled by script validation
            }
        }

        string? srtContent = null;
        try
        {
            srtContent = await subtitleWorkflow.GetContentAsync(contentProjectId, cancellationToken);
        }
        catch
        {
            // Subtitle is optional
        }

        return PreRenderQualityChecker.Inspect(scriptResult, srtContent, targetDurationSeconds);
    }
}

public sealed record PreRenderIssue(
    string Severity,
    string Category,
    string Message);

public sealed record PreRenderCheckReport(
    bool Passed,
    int ScriptWordCount,
    double EstimatedSpeechSeconds,
    int SubtitleCueCount,
    IReadOnlyList<PreRenderIssue> Issues);
