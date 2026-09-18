using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateStoryboardApiContractTests
{
    [Fact]
    public void JobResponse_ExposesCompletedStructuredStoryboardResult()
    {
        var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var details = new JobDetails(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobType.GenerateStoryboard,
            JobStatus.Succeeded,
            0,
            2,
            GenerateStoryboardResult.Deserialize(GenerateStoryboardTestData.ValidResult),
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

        Assert.Equal("GenerateStoryboard", json.GetProperty("type").GetString());
        Assert.Equal("completed", json.GetProperty("status").GetString());
        var result = json.GetProperty("result");
        Assert.Equal("Local AI Storyboard", result.GetProperty("title").GetString());
        Assert.Equal(2, result.GetProperty("scenes").GetArrayLength());
    }
}
