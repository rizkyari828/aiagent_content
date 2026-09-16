using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateIdea;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateIdeaContractsTests
{
    [Fact]
    public void Payload_DeserializesAndNormalizesValidInput()
    {
        var projectId = Guid.NewGuid();

        var payload = GenerateIdeaJobPayload.Deserialize(
            $$"""
            {
              "contentProjectId": "{{projectId}}",
              "topic": "  Local-first content  ",
              "targetAudience": " Creators ",
              "language": " Indonesian ",
              "model": " configured-model "
            }
            """);

        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal("Local-first content", payload.Topic);
        Assert.Equal("Creators", payload.TargetAudience);
        Assert.Equal("Indonesian", payload.Language);
        Assert.Equal("configured-model", payload.Model);
    }

    [Fact]
    public void Payload_RejectsMalformedJson()
    {
        var exception = Assert.Throws<JobExecutionException>(
            () => GenerateIdeaJobPayload.Deserialize("not-json"));

        Assert.Equal("generate_idea_invalid_payload", exception.ErrorCode);
    }

    [Fact]
    public void Result_DeserializesAndSerializesCanonicalSchema()
    {
        var result = GenerateIdeaResult.Deserialize(
            """
            {
              "title": " Local AI Content ",
              "hook": "Create privately.",
              "summary": "A local workflow.",
              "angle": "Privacy",
              "targetAudience": "Creators",
              "suggestedFormat": "Tutorial"
            }
            """);

        Assert.Equal("Local AI Content", result.Title);
        Assert.Contains("\"suggestedFormat\":\"Tutorial\"", result.Serialize());
    }

    [Fact]
    public void Result_RejectsMalformedSchema()
    {
        var exception = Assert.Throws<JobExecutionException>(
            () => GenerateIdeaResult.Deserialize("""{"title":"Incomplete"}"""));

        Assert.Equal("generate_idea_invalid_result", exception.ErrorCode);
    }
}
