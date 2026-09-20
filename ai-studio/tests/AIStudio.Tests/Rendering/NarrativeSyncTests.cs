using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Application.Rendering.Narration;
using AIStudio.Application.Rendering.Visuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Narrative-source, scene-timing and subtitle synchronization behavior. Fakes only.
/// </summary>
public sealed class NarrativeSyncTests
{
    [Fact]
    public void ReviewedScript_MapsOpeningSectionsAndClosingPositionally()
    {
        var script = ReviewedNarrationScript.Parse(
            """
            {
              "title": "Test",
              "openingHook": "Pembuka.",
              "sections": [
                { "heading": "Kenapa Lokal", "narration": "Penjelasan satu." },
                { "heading": "Langkah Satu", "narration": "Penjelasan dua." }
              ],
              "closing": "Penutup."
            }
            """);
        var storyboard = GenerateStoryboardResult.Deserialize(
            """
            {
              "title": "Test",
              "scenes": [
                { "heading": "Opening Hook", "visual": "A." },
                { "heading": "Kenapa Lokal", "visual": "B." },
                { "heading": "Langkah Satu", "visual": "C." },
                { "heading": "Closing", "visual": "D." }
              ]
            }
            """);

        var narration = script.MapToScenes(storyboard);

        Assert.Equal(4, narration.Count);
        Assert.Equal("Pembuka.", narration[0].Text);
        Assert.Equal("opening", narration[0].Kind);
        Assert.Equal("Penjelasan satu.", narration[1].Text);
        Assert.Equal("section", narration[1].Kind);
        Assert.Equal("Penjelasan dua.", narration[2].Text);
        Assert.Equal("Penutup.", narration[3].Text);
        Assert.Equal("closing", narration[3].Kind);
    }

    [Fact]
    public void ReviewedScript_FailsWhenNoReliableMappingExists()
    {
        var script = ReviewedNarrationScript.Parse(
            """
            {
              "title": "Test",
              "openingHook": "",
              "sections": [
                { "heading": "Kenapa Lokal", "narration": "Penjelasan." }
              ],
              "closing": ""
            }
            """);
        var storyboard = GenerateStoryboardResult.Deserialize(
            """
            {
              "title": "Test",
              "scenes": [
                { "heading": "Opening Hook", "visual": "A." },
                { "heading": "Kenapa Lokal", "visual": "B." },
                { "heading": "Extra", "visual": "C." }
              ]
            }
            """);

        var exception = Assert.Throws<AudioProductionException>(
            () => script.MapToScenes(storyboard));

        Assert.Equal("audio_narration_source_unmapped", exception.ErrorCode);
    }

    [Fact]
    public void ReviewedScript_RejectsMalformedContent()
    {
        var exception = Assert.Throws<AudioProductionException>(
            () => ReviewedNarrationScript.Parse("not-json"));

        Assert.Equal("audio_narration_source_invalid", exception.ErrorCode);
    }

    [Fact]
    public void NarrationText_NormalizesQuotesAndWhitespace()
    {
        var normalized = NarrationText.Normalize(
            "Buka browser, ketik 'ollama.com'  di\naddress bar.");

        Assert.Equal("Buka browser, ketik ollama.com di address bar.", normalized);
        Assert.DoesNotContain("'", normalized);
    }

    [Fact]
    public void SubtitleTimeline_KeepsCuesInsideTheirSceneAndWrapsLines()
    {
        var scenes = new[]
        {
            new NarrationSubtitleScene(0, "Satu dua tiga. Empat lima enam!", 1.0, 3.0),
            new NarrationSubtitleScene(1, "Tujuh delapan sembilan?", 4.0, 2.0)
        };

        var srt = SubtitleTimeline.BuildFromNarration(scenes);
        var cues = ParseCues(srt);

        Assert.NotEmpty(cues);
        Assert.All(cues, cue => Assert.True(cue.End > cue.Start));
        // Scene 0 window is [1.0, 4.0]; scene 1 window is [4.0, 6.0].
        Assert.All(cues, cue => Assert.InRange(cue.Start, 1.0, 6.0));
        Assert.All(cues, cue => Assert.InRange(cue.End, 1.0, 6.0));
        Assert.All(cues, cue => Assert.All(
            cue.Text.Split('\n'),
            line => Assert.True(line.Length <= NarrativeTiming.MaxSubtitleLineCharacters)));
    }

    [Fact]
    public void SubtitleTimeline_DoesNotBleedBetweenScenes()
    {
        var scenes = new[]
        {
            new NarrationSubtitleScene(0, "Pertama. Kedua.", 0.0, 2.0),
            new NarrationSubtitleScene(1, "Ketiga. Keempat.", 2.0, 2.0)
        };

        var cues = ParseCues(SubtitleTimeline.BuildFromNarration(scenes));

        // Every cue from the first scene ends at or before the second scene starts.
        Assert.All(cues.Where(cue => cue.Start < 2.0), cue => Assert.True(cue.End <= 2.0 + 0.001));
        Assert.All(cues.Where(cue => cue.Start >= 2.0), cue => Assert.True(cue.Start >= 2.0 - 0.001));
    }

    [Fact]
    public void Choreography_SchedulesBeatsInsideTheNarrationWindow()
    {
        var brief = new SceneVisualBrief(
            2,
            "Video 1",
            "Yang Kamu Butuhkan",
            SceneVisualLayout.Cards,
            SceneVisualPalette.Ocean,
            [
                new SceneVisualCard("Satu"),
                new SceneVisualCard("Dua"),
                new SceneVisualCard("Tiga")
            ]);
        var window = new SceneNarrationWindow(2, NarrationStartWithinScene: 0.5, NarrationDurationSeconds: 4);

        var choreography = SceneChoreographyPlanner.Build(brief, 5, window);

        Assert.True(choreography.IsValid());
        Assert.All(
            choreography.Beats,
            beat => Assert.True(beat.StartTime >= 0.5 - 0.001));
        Assert.All(
            choreography.Beats,
            beat => Assert.True(beat.StartTime + beat.Duration <= 5.0 + 0.001));
    }

    [Fact]
    public void ImagePrompt_ForbidsGeneratedTypography()
    {
        var brief = new SceneVisualBrief(
            7,
            "Video 1",
            "Closing",
            SceneVisualLayout.Hero,
            SceneVisualPalette.Slate,
            [new SceneVisualCard("AI Lokal")]);

        var prompt = SceneImagePrompt.Build(brief);

        Assert.Contains("no text", prompt);
        Assert.Contains("no letters", prompt);
        Assert.Contains("no logos", prompt);
        Assert.Contains("blank unlabeled screens", prompt);
    }

    [Fact]
    public void Overlay_ProvidesTrustedTypographyForImages()
    {
        var svg = SceneVisualSvg.ComposeOverlay(
            "AI LOKAL",
            SceneVisualPalette.Slate,
            opacity: 1,
            scale: 1);

        Assert.Contains("AI LOKAL", svg);
    }

    private static IReadOnlyList<(double Start, double End, string Text)> ParseCues(string srt)
    {
        var cues = new List<(double, double, string)>();
        var blocks = srt.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3)
            {
                continue;
            }

            var arrow = lines[1].Split(" --> ");
            cues.Add((ParseTimestamp(arrow[0]), ParseTimestamp(arrow[1]), string.Join('\n', lines.Skip(2))));
        }

        return cues;
    }

    private static double ParseTimestamp(string value)
    {
        var parts = value.Split(':');
        var seconds = double.Parse(parts[2].Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        return (int.Parse(parts[0]) * 3600) + (int.Parse(parts[1]) * 60) + seconds;
    }
}
