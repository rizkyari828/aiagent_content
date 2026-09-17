using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateScript;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateScriptContractsTests
{
    [Fact]
    public void Payload_DeserializesAndNormalizesSelectedIdea()
    {
        var projectId = Guid.NewGuid();
        var payload = GenerateScriptJobPayload.Deserialize(
            GenerateScriptTestData.ValidPayload(projectId));

        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal("Local AI Content", payload.SelectedIdea.Title);
        Assert.Equal("Indonesian", payload.Language);
    }

    [Fact]
    public void Payload_DefaultsLanguageAndRejectsMissingIdea()
    {
        var projectId = Guid.NewGuid();
        var valid = GenerateScriptJobPayload.Deserialize(
            $$"""
            {
              "contentProjectId": "{{projectId}}",
              "selectedIdea": {
                "title": "Title",
                "hook": "Hook",
                "summary": "Summary",
                "angle": "Angle",
                "targetAudience": "Audience",
                "suggestedFormat": "Tutorial"
              }
            }
            """);
        Assert.Equal("Indonesian", valid.Language);

        var exception = Assert.Throws<JobExecutionException>(
            () => GenerateScriptJobPayload.Deserialize(
                $$"""{"contentProjectId":"{{projectId}}"}"""));
        Assert.Equal("generate_script_invalid_payload", exception.ErrorCode);
    }

    [Fact]
    public void Result_DeserializesAndSerializesCanonicalSchema()
    {
        var result = GenerateScriptResult.Deserialize(GenerateScriptTestData.ValidResult);
        var roundTrip = GenerateScriptResult.Deserialize(result.Serialize());

        Assert.Equal("Local AI Tutorial", roundTrip.Title);
        Assert.Equal(2, roundTrip.Sections.Count);
        Assert.Equal("Why local AI", roundTrip.Sections[0].Heading);
        Assert.Contains("verify its limits", roundTrip.Closing);
    }

    [Fact]
    public void Result_RejectsMissingOrEmptySections()
    {
        var missing = Assert.Throws<JobExecutionException>(
            () => GenerateScriptResult.Deserialize("""{"title":"Incomplete"}"""));
        Assert.Equal("generate_script_invalid_result", missing.ErrorCode);

        var empty = Assert.Throws<JobExecutionException>(
            () => GenerateScriptResult.Deserialize(
                """
                {
                  "title": "Title",
                  "openingHook": "Hook",
                  "sections": [],
                  "closing": "Closing"
                }
                """));
        Assert.Equal("generate_script_invalid_result", empty.ErrorCode);
    }
}
