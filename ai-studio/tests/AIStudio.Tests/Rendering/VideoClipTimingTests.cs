using AIStudio.Application.Rendering.Visuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Deterministic clip-fitting math only: no renderer, handler, or FFmpeg is involved.
/// </summary>
public sealed class VideoClipTimingTests
{
    [Fact]
    public void Fit_LoopsShorterClipAndTrimsTheRoundingTail()
    {
        var timing = VideoClipTiming.Fit(sourceClipSeconds: 3, sceneContentSeconds: 7);

        Assert.Equal(7, timing.TargetSeconds);
        Assert.Equal(3, timing.PlayCount);
        Assert.Equal(2, timing.RepeatCount);
        Assert.Equal(9, timing.PlayedSeconds);
        Assert.Equal(2, timing.TrimmedTailSeconds);
        Assert.Equal(7, timing.CoveredSeconds);
        Assert.Equal(0, timing.RoundingResidualSeconds);
    }

    [Fact]
    public void Fit_PlaysExactMatchOnceWithoutTrim()
    {
        var timing = VideoClipTiming.Fit(sourceClipSeconds: 3, sceneContentSeconds: 3);

        Assert.Equal(1, timing.PlayCount);
        Assert.Equal(0, timing.RepeatCount);
        Assert.Equal(0, timing.TrimmedTailSeconds);
        Assert.Equal(3, timing.PlayedSeconds);
        Assert.Equal(3, timing.CoveredSeconds);
    }

    [Fact]
    public void Fit_TrimsLongerClipAtTheTail()
    {
        var timing = VideoClipTiming.Fit(sourceClipSeconds: 5, sceneContentSeconds: 2);

        Assert.Equal(1, timing.PlayCount);
        Assert.Equal(0, timing.RepeatCount);
        Assert.Equal(3, timing.TrimmedTailSeconds);
        Assert.Equal(5, timing.PlayedSeconds);
        Assert.Equal(2, timing.CoveredSeconds);
    }

    [Fact]
    public void Fit_HandlesVeryShortSceneWithoutFreezing()
    {
        var timing = VideoClipTiming.Fit(sourceClipSeconds: 4, sceneContentSeconds: 0.5);

        Assert.Equal(1, timing.PlayCount);
        Assert.Equal(3.5, timing.TrimmedTailSeconds);
        Assert.Equal(0.5, timing.CoveredSeconds);
        Assert.Equal(4, timing.PlayedSeconds);
    }

    [Fact]
    public void Fit_AddsCrossfadeOverlapToTheTargetTimeline()
    {
        var timing = VideoClipTiming.Fit(
            sourceClipSeconds: 2.8,
            sceneContentSeconds: 2.8,
            crossfadeOverlapSeconds: 0.35);

        Assert.Equal(3.15, timing.TargetSeconds);
        Assert.Equal(2, timing.PlayCount);
        Assert.Equal(5.6, timing.PlayedSeconds);
        Assert.Equal(2.45, timing.TrimmedTailSeconds);
        Assert.Equal(3.15, timing.CoveredSeconds);
    }

    [Fact]
    public void Fit_OverlapThatReachesAnExactMultipleNeedsNoTrim()
    {
        var timing = VideoClipTiming.Fit(
            sourceClipSeconds: 3,
            sceneContentSeconds: 5.65,
            crossfadeOverlapSeconds: 0.35);

        Assert.Equal(6, timing.TargetSeconds);
        Assert.Equal(2, timing.PlayCount);
        Assert.Equal(0, timing.TrimmedTailSeconds);
        Assert.Equal(6, timing.CoveredSeconds);
    }

    [Fact]
    public void Fit_RoundsDeterministicallyToThreeDecimals()
    {
        var timing = VideoClipTiming.Fit(
            sourceClipSeconds: 1.0004,
            sceneContentSeconds: 2.9996,
            crossfadeOverlapSeconds: 0.0004);

        Assert.Equal(1, timing.SourceClipSeconds);
        Assert.Equal(3, timing.SceneContentSeconds);
        Assert.Equal(0, timing.CrossfadeOverlapSeconds);
        Assert.Equal(3, timing.TargetSeconds);
        Assert.Equal(3, timing.PlayCount);
        Assert.Equal(2, timing.RepeatCount);
        Assert.Equal(0, timing.TrimmedTailSeconds);
    }

    [Fact]
    public void Fit_ReportsTheSignedRoundingShiftOfTheRequestedTimeline()
    {
        var timing = VideoClipTiming.Fit(
            sourceClipSeconds: 2,
            sceneContentSeconds: 2.0004);

        Assert.Equal(2, timing.TargetSeconds);
        Assert.Equal(-0.0004, timing.RoundingResidualSeconds, precision: 6);
    }

    [Fact]
    public void Fit_IsDeterministicAcrossRepeatedCalls()
    {
        var first = VideoClipTiming.Fit(2.5, 11.3, 0.35);
        var second = VideoClipTiming.Fit(2.5, 11.3, 0.35);

        Assert.Equal(first, second);
        Assert.Equal(11.65, first.TargetSeconds);
        Assert.Equal(5, first.PlayCount);
        Assert.Equal(4, first.RepeatCount);
        Assert.Equal(12.5, first.PlayedSeconds);
        Assert.Equal(0.85, first.TrimmedTailSeconds);
    }

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(-1, 3, 0)]
    [InlineData(double.NaN, 3, 0)]
    [InlineData(double.PositiveInfinity, 3, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(3, -1, 0)]
    [InlineData(3, double.NaN, 0)]
    [InlineData(3, 3, -0.1)]
    [InlineData(3, 3, double.PositiveInfinity)]
    public void Fit_RejectsInvalidInput(
        double sourceClipSeconds,
        double sceneContentSeconds,
        double crossfadeOverlapSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoClipTiming.Fit(
            sourceClipSeconds,
            sceneContentSeconds,
            crossfadeOverlapSeconds));
    }
}
