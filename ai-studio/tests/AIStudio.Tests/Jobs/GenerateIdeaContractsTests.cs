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
    public void Result_Serialize_NormalizesAllFields()
    {
        var result = new GenerateIdeaResult(
            Title: "  Title  ",
            Hook: "  Hook  ",
            Summary: "  Summary  ",
            Angle: "  Angle  ",
            TargetAudience: "  Audience  ",
            SuggestedFormat: "  Format  ");

        var roundTrip = GenerateIdeaResult.Deserialize(result.Serialize());

        Assert.Equal("Title", roundTrip.Title);
        Assert.Equal("Hook", roundTrip.Hook);
        Assert.Equal("Summary", roundTrip.Summary);
        Assert.Equal("Angle", roundTrip.Angle);
        Assert.Equal("Audience", roundTrip.TargetAudience);
        Assert.Equal("Format", roundTrip.SuggestedFormat);
    }

    [Fact]
    public void Result_RejectsMalformedSchema()
    {
        var exception = Assert.Throws<JobExecutionException>(
            () => GenerateIdeaResult.Deserialize("""{"title":"Incomplete"}"""));

        Assert.Equal("generate_idea_invalid_result", exception.ErrorCode);
    }

    [Fact]
    public void Result_Serialize_RejectsBlankField()
    {
        var result = new GenerateIdeaResult(
            Title: "Valid",
            Hook: "",
            Summary: "Valid",
            Angle: "Valid",
            TargetAudience: "Valid",
            SuggestedFormat: "Valid");

        var exception = Assert.Throws<JobExecutionException>(() => result.Serialize());
        Assert.Equal("generate_idea_invalid_result", exception.ErrorCode);
    }

    [Fact]
    public void Result_Serialize_RejectsWhitespaceOnlyField()
    {
        var result = new GenerateIdeaResult(
            Title: "   ",
            Hook: "Valid",
            Summary: "Valid",
            Angle: "Valid",
            TargetAudience: "Valid",
            SuggestedFormat: "Valid");

        var exception = Assert.Throws<JobExecutionException>(() => result.Serialize());
        Assert.Equal("generate_idea_invalid_result", exception.ErrorCode);
    }

    [Fact]
    public void Result_Serialize_RejectsOverLengthField()
    {
        var longString = new string('x', 2_001);
        var result = new GenerateIdeaResult(
            Title: longString,
            Hook: "Valid",
            Summary: "Valid",
            Angle: "Valid",
            TargetAudience: "Valid",
            SuggestedFormat: "Valid");

        var exception = Assert.Throws<JobExecutionException>(() => result.Serialize());
        Assert.Equal("generate_idea_invalid_result", exception.ErrorCode);
    }
}
