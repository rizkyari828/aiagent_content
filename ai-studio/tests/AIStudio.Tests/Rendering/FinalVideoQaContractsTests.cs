using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class FinalVideoQaContractsTests
{
    [Fact]
    public void FinalVideoQaResult_RoundTripsAndCanonicalizesMetadata()
    {
        var result = new FinalVideoQaResult(
            "  renders/project/video.mp4  ",
            new string('D', FinalVideoQaResult.ContentHashLength),
            42.5,
            1280,
            720,
            HasVideo: true,
            HasAudio: true,
            HasSubtitle: true);

        var roundTripped = FinalVideoQaResult.Deserialize(result.Serialize());

        Assert.Equal("renders/project/video.mp4", roundTripped.OutputPath);
        Assert.Equal(new string('d', FinalVideoQaResult.ContentHashLength), roundTripped.ContentHash);
        Assert.Equal(42.5, roundTripped.DurationSeconds);
        Assert.Equal(1280, roundTripped.Width);
        Assert.Equal(720, roundTripped.Height);
        Assert.True(roundTripped.HasSubtitle);
    }

    [Fact]
    public void FinalVideoQaResult_RejectsHashThatIsNotSha256()
    {
        var result = new FinalVideoQaResult(
            "renders/project/video.mp4",
            "not-a-hash",
            10,
            1280,
            720,
            HasVideo: true,
            HasAudio: true,
            HasSubtitle: false);

        var exception = Assert.Throws<JobExecutionException>(
            () => FinalVideoQaResult.Deserialize(result.Serialize()));

        Assert.Equal("final_video_qa_invalid_result", exception.ErrorCode);
    }

    [Fact]
    public void JobResponse_ExposesCompletedFinalVideoQaResult()
    {
        var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var result = new FinalVideoQaResult(
            "renders/project/video.mp4",
            new string('d', FinalVideoQaResult.ContentHashLength),
            30,
            1280,
            720,
            HasVideo: true,
            HasAudio: true,
            HasSubtitle: true);
        var details = new JobDetails(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobType.FinalVideoQa,
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

        Assert.Equal("FinalVideoQa", json.GetProperty("type").GetString());
        Assert.Equal("completed", json.GetProperty("status").GetString());
        var qaResult = json.GetProperty("result");
        Assert.Equal("renders/project/video.mp4", qaResult.GetProperty("outputPath").GetString());
        Assert.True(qaResult.GetProperty("hasSubtitle").GetBoolean());
    }
}
