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

    private static void AssertClose(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index], actual[index], 3);
        }
    }
}
