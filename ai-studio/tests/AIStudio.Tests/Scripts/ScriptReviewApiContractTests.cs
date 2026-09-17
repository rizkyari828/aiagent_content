using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Scripts;
using AIStudio.Domain.Scripts;
using AIStudio.Tests.Jobs;
using Xunit;

namespace AIStudio.Tests.Scripts;

public sealed class ScriptReviewApiContractTests
{
    [Fact]
    public void Response_ExposesCanonicalReviewedScriptWithoutWorkerInternals()
    {
        var now = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new ReviewedScriptSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            GenerateScriptResult.Deserialize(GenerateScriptTestData.ValidResult) with
            {
                Closing = "Human approved closing."
            },
            ScriptReviewStatus.Approved,
            2,
            now,
            now.AddMinutes(2),
            now.AddMinutes(2));

        var response = ReviewedScriptResponse.From(snapshot);
        var json = JsonSerializer.SerializeToElement(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("approved", json.GetProperty("status").GetString());
        Assert.Equal(2, json.GetProperty("revision").GetInt32());
        Assert.Equal(
            "Human approved closing.",
            json.GetProperty("script").GetProperty("closing").GetString());
        Assert.False(json.TryGetProperty("workerId", out _));
        Assert.False(json.TryGetProperty("jobResult", out _));
    }
}
