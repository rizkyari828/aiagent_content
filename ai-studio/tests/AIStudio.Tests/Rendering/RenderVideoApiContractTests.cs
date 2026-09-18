using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class RenderVideoApiContractTests
{
    [Fact]
    public void RenderVideoResult_RoundTripsCanonicalMetadata()
    {
        var result = new RenderVideoResult(
            $"renders/{Guid.NewGuid():N}/video.mp4",
            new string('d', RenderVideoResult.ContentHashLength),
            1_048_576,
            42.5,
            1280,
            720,
            Guid.NewGuid(),
            Guid.NewGuid(),
            3,
            new string('e', RenderVideoResult.ContentHashLength));

        var roundTripped = RenderVideoResult.Deserialize(result.Serialize());

        Assert.Equal(result.OutputPath, roundTripped.OutputPath);
        Assert.Equal(result.ContentHash, roundTripped.ContentHash);
        Assert.Equal(result.ByteSize, roundTripped.ByteSize);
        Assert.Equal(result.DurationSeconds, roundTripped.DurationSeconds);
        Assert.Equal(result.SceneCount, roundTripped.SceneCount);
        Assert.Equal(result.NarrationContentHash, roundTripped.NarrationContentHash);
    }

    [Fact]
    public void JobResponse_ExposesCompletedRenderVideoResult()
    {
        var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var result = new RenderVideoResult(
            "renders/project/video.mp4",
            new string('d', RenderVideoResult.ContentHashLength),
            2_048,
            30,
            1280,
            720,
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            new string('e', RenderVideoResult.ContentHashLength));
        var details = new JobDetails(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobType.RenderVideo,
            JobStatus.Succeeded,
            0,
            2,
            result,
            null,
            null,
            now,
            now,
            now,
            now);

        var response = JobResponse.From(details);
        var json = JsonSerializer.SerializeToElement(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("RenderVideo", json.GetProperty("type").GetString());
        Assert.Equal("completed", json.GetProperty("status").GetString());
        var renderResult = json.GetProperty("result");
        Assert.Equal("renders/project/video.mp4", renderResult.GetProperty("outputPath").GetString());
        Assert.Equal(2, renderResult.GetProperty("sceneCount").GetInt32());
    }
}
