using AIStudio.Application.Rendering;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class SceneTimingTests
{
    [Fact]
    public void Allocate_SplitsProportionallyWhenNoMinimumBinds()
    {
        var result = SceneTiming.Allocate([1, 1, 2], 20, [0, 0, 0]);

        AssertClose([5, 5, 10], result);
        Assert.Equal(20, result.Sum(), 3);
    }

    [Fact]
    public void Allocate_RaisesBelowMinimumAndRedistributes()
    {
        var result = SceneTiming.Allocate([1, 1, 1], 12, [2, 6, 2]);

        AssertClose([3, 6, 3], result);
        Assert.All(result, value => Assert.True(value >= 2 - 1e-9));
        Assert.Equal(12, result.Sum(), 3);
    }

    [Fact]
    public void Allocate_FallsBackToProportionalWhenMinimumsDoNotFit()
    {
        var result = SceneTiming.Allocate([1, 1], 3, [2, 2]);

        AssertClose([1.5, 1.5], result);
        Assert.Equal(3, result.Sum(), 3);
    }

    [Fact]
    public void Allocate_KeepsTotalAndFloorsForManyScenes()
    {
        var weights = new double[] { 312, 478, 381, 440, 333, 393, 373, 307 };
        var minimums = new double[] { 3.5, 3.5, 2, 2, 2, 2, 2, 2 };

        var result = SceneTiming.Allocate(weights, 24, minimums);

        Assert.Equal(24, result.Sum(), 3);
        Assert.True(result[0] >= 3.5 - 1e-9);
        Assert.True(result[1] >= 3.5 - 1e-9);
        Assert.All(result, value => Assert.True(value >= 2 - 1e-9));
    }

    [Fact]
    public void Allocate_RejectsNonPositiveTotal()
    {
        var exception = Assert.Throws<RenderVideoException>(
            () => SceneTiming.Allocate([1], 0, [1]));

        Assert.Equal("render_narration_duration_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Allocate_RejectsMismatchedMinimums()
    {
        Assert.Throws<ArgumentException>(() => SceneTiming.Allocate([1, 1], 10, [1]));
    }

    [Fact]
    public void WeightFor_IsProportionalToTextAndNeverZero()
    {
        Assert.Equal(SceneTiming.MinimumWeight, SceneTiming.WeightFor(null, null));
        Assert.True(SceneTiming.WeightFor("Longer heading", "and a longer visual") >
            SceneTiming.WeightFor("Short", "x"));
    }

    [Fact]
    public void AnimationFloorIsHigherThanDefaultFloor()
    {
        Assert.True(SceneTiming.AnimationMinimumSeconds > SceneTiming.DefaultMinimumSeconds);
    }

    [Fact]
    public void AllocateForScenes_UsesAnimatedFloorAndSumsToNarration()
    {
        var result = SceneTiming.AllocateForScenes(
            ["Opening", "Install"],
            ["a much longer visual description", "short"],
            [true, false],
            24);

        Assert.Equal(24, result.Sum(), 3);
        Assert.True(result[0] >= SceneTiming.AnimationMinimumSeconds - 1e-9);
        Assert.True(result[1] >= SceneTiming.DefaultMinimumSeconds - 1e-9);
    }

    [Fact]
    public void AllocateForScenes_RejectsMismatchedLengths()
    {
        Assert.Throws<ArgumentException>(() =>
            SceneTiming.AllocateForScenes(["a"], ["b", "c"], [true, false], 10));
    }

    [Fact]
    public void SubtitleTimeline_DerivesCumulativeBoundaries()
    {
        var srt = SubtitleTimeline.Build(["A", "B"], [2.5, 3.5]);

        Assert.Contains("00:00:00,000 --> 00:00:02,500", srt);
        Assert.Contains("00:00:02,500 --> 00:00:06,000", srt);
        Assert.Contains("A", srt);
        Assert.Contains("B", srt);
    }

    [Fact]
    public void SubtitleTimeline_RejectsMismatchedLengths()
    {
        Assert.Throws<ArgumentException>(() => SubtitleTimeline.Build(["A"], [1, 2]));
    }

    [Fact]
    public void SubtitleTimeline_ReadsCanonicalCueTexts()
    {
        var cues = SubtitleTimeline.ReadCueTexts(
            "1\n00:00:00,000 --> 00:00:02,000\nHello\n\n2\n00:00:02,000 --> 00:00:04,000\nWorld\nline two\n");

        Assert.Equal(["Hello", "World\nline two"], cues);
    }

    private static void AssertClose(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index], actual[index], 3);
        }
    }
}
