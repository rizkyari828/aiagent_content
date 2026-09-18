using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateStoryboard;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateStoryboardContractsTests
{
    [Fact]
    public void Payload_DeserializesContentProjectIdAndRejectsMissingIdentifier()
    {
        var projectId = Guid.NewGuid();
        var payload = GenerateStoryboardJobPayload.Deserialize(
            GenerateStoryboardTestData.ValidPayload(projectId));

        Assert.Equal(projectId, payload.ContentProjectId);

        var missing = Assert.Throws<JobExecutionException>(
            () => GenerateStoryboardJobPayload.Deserialize("{}"));
        Assert.Equal("generate_storyboard_invalid_payload", missing.ErrorCode);

        var empty = Assert.Throws<JobExecutionException>(
            () => GenerateStoryboardJobPayload.Deserialize(
                $$"""{"contentProjectId":"{{Guid.Empty}}"}"""));
        Assert.Equal("generate_storyboard_invalid_payload", empty.ErrorCode);
    }

    [Fact]
    public void Result_DeserializesAndSerializesCanonicalSchema()
    {
        var result = GenerateStoryboardResult.Deserialize(
            GenerateStoryboardTestData.ValidResult);
        var roundTrip = GenerateStoryboardResult.Deserialize(result.Serialize());

        Assert.Equal("Local AI Storyboard", roundTrip.Title);
        Assert.Equal(2, roundTrip.Scenes.Count);
        Assert.Equal("Why local AI", roundTrip.Scenes[0].Heading);
        Assert.Contains("job queue", roundTrip.Scenes[1].Visual);
        Assert.Equal(result.Serialize(), roundTrip.Serialize());
    }

    [Fact]
    public void Result_RejectsMissingOrEmptyScenes()
    {
        var missing = Assert.Throws<JobExecutionException>(
            () => GenerateStoryboardResult.Deserialize("""{"title":"Incomplete"}"""));
        Assert.Equal("generate_storyboard_invalid_result", missing.ErrorCode);

        var empty = Assert.Throws<JobExecutionException>(
            () => GenerateStoryboardResult.Deserialize(
                """{"title":"Title","scenes":[]}"""));
        Assert.Equal("generate_storyboard_invalid_result", empty.ErrorCode);
    }

    [Fact]
    public void Result_RejectsSceneWithoutHeadingOrVisual()
    {
        var missingVisual = Assert.Throws<JobExecutionException>(
            () => GenerateStoryboardResult.Deserialize(
                """{"title":"Title","scenes":[{"heading":"Scene"}]}"""));
        Assert.Equal("generate_storyboard_invalid_result", missingVisual.ErrorCode);

        var blankVisual = Assert.Throws<JobExecutionException>(
            () => GenerateStoryboardResult.Deserialize(
                """{"title":"Title","scenes":[{"heading":"Scene","visual":"   "}]}"""));
        Assert.Equal("generate_storyboard_invalid_result", blankVisual.ErrorCode);
    }

    [Fact]
    public void Result_PreservesSceneOrderingAndTrimsCanonicalText()
    {
        var result = GenerateStoryboardResult.Deserialize(
            """
            {
              "title": "  Ordered  ",
              "scenes": [
                { "heading": " First ", "visual": " One " },
                { "heading": "Second", "visual": "Two" },
                { "heading": "Third", "visual": "Three" }
              ]
            }
            """);

        Assert.Equal("Ordered", result.Title);
        Assert.Equal(["First", "Second", "Third"], result.Scenes.Select(scene => scene.Heading));
        Assert.Equal(["One", "Two", "Three"], result.Scenes.Select(scene => scene.Visual));
    }
}
