using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateScriptApiContractTests
{
    [Fact]
    public void JobResponse_ExposesCompletedStructuredScriptResult()
    {
        var now = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        var details = new JobDetails(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobType.GenerateScript,
            JobStatus.Succeeded,
            0,
            2,
            GenerateScriptResult.Deserialize(GenerateScriptTestData.ValidResult),
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

        Assert.Equal("GenerateScript", json.GetProperty("type").GetString());
        Assert.Equal("completed", json.GetProperty("status").GetString());
        var result = json.GetProperty("result");
        Assert.Equal("Local AI Tutorial", result.GetProperty("title").GetString());
        Assert.Equal(2, result.GetProperty("sections").GetArrayLength());
    }
}
