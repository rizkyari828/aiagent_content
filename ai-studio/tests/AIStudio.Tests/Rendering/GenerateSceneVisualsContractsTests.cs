using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GenerateSceneVisualsContractsTests
{
    [Fact]
    public void Result_RoundTripsValidVisuals()
    {
        var result = new GenerateSceneVisualsResult(
            Guid.NewGuid(),
            SceneCount: 3,
            GeneratedCount: 2,
            SkippedCount: 1,
            Visuals:
            [
                new GeneratedSceneVisual(0, "SvgStill", "None", "visuals/a/scene_0.png", 120, new string('a', 64)),
                new GeneratedSceneVisual(2, "ManimAnimation", "ChatFlow", "visuals/a/scene_2.mp4", 2048, new string('b', 64))
            ]);

        var roundTripped = GenerateSceneVisualsResult.Deserialize(result.Serialize());

        Assert.Equal(result.StoryboardJobId, roundTripped.StoryboardJobId);
        Assert.Equal(2, roundTripped.GeneratedCount);
        Assert.Equal(1, roundTripped.SkippedCount);
        Assert.Equal(2, roundTripped.Visuals.Count);
        Assert.Equal("ManimAnimation", roundTripped.Visuals[1].Engine);
    }

    [Fact]
    public void Result_RejectsMismatchedCounts()
    {
        var result = new GenerateSceneVisualsResult(
            Guid.NewGuid(),
            2,
            2,
            1,
            [
                new GeneratedSceneVisual(0, "SvgStill", "None", "visuals/a/scene_0.png", 120, new string('a', 64))
            ]);

        var exception = Assert.Throws<JobExecutionException>(
            () => GenerateSceneVisualsResult.Deserialize(result.Serialize()));

        Assert.Equal("visual_result_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Result_RejectsInvalidHash()
    {
        var result = new GenerateSceneVisualsResult(
            Guid.NewGuid(),
            1,
            1,
            0,
            [
                new GeneratedSceneVisual(0, "SvgStill", "None", "visuals/a/scene_0.png", 120, "not-a-hash")
            ]);

        var exception = Assert.Throws<JobExecutionException>(
            () => GenerateSceneVisualsResult.Deserialize(result.Serialize()));

        Assert.Equal("visual_result_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Payload_RejectsMissingStoryboard()
    {
        var exception = Assert.Throws<JobExecutionException>(
            () => GenerateSceneVisualsJobPayload.Deserialize(
                $$"""{"contentProjectId":"{{Guid.NewGuid()}}"}"""));

        Assert.Equal("visual_job_invalid_payload", exception.ErrorCode);
    }

    [Fact]
    public void Payload_RejectsInvalidJson()
    {
        var exception = Assert.Throws<JobExecutionException>(
            () => GenerateSceneVisualsJobPayload.Deserialize("{not-json"));

        Assert.Equal("visual_job_invalid_payload", exception.ErrorCode);
    }
}
