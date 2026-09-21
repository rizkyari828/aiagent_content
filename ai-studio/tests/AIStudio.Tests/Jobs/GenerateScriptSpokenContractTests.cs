using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Creative;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Stories;
using AIStudio.Tests.Stories;
using AIStudio.Tests.StoryContext;
using Xunit;

namespace AIStudio.Tests.Jobs;

/// <summary>
/// The Script prompt must define the spoken fields explicitly and forbid
/// production/visual direction inside them, both with and without StoryContext.
/// </summary>
public sealed class GenerateScriptSpokenContractTests
{
    [Fact]
    public async Task PromptDefinesSpokenFieldsAndForbidsProductionDirections()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateScriptTestData.ValidResult);
        var handler = CreateHandler(projectId, generator);

        await handler.ExecuteAsync(
            GenerateScriptTestData.CreateJob(
                projectId,
                GenerateScriptTestData.ValidPayload(projectId)),
            TestContext.Current.CancellationToken);

        AssertSpokenContract(generator.Request!.Prompt);
    }

    [Fact]
    public async Task PromptKeepsTheSpokenContractWhenStoryContextIsSupplied()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateScriptTestData.ValidResult);
        var handler = CreateHandler(projectId, generator);

        await handler.ExecuteAsync(
            GenerateScriptTestData.CreateJob(
                projectId,
                Payload(
                    projectId,
                    StoryContextTestSupport.Direction(),
                    StoryContextTestSupport.ThreeBeatPlan())),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;
        Assert.Contains("Narrative planning context", prompt);
        AssertSpokenContract(prompt);
    }

    [Fact]
    public async Task PromptTranslatesAudiovisualCuesIntoSpokenWords()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateScriptTestData.ValidResult);
        var handler = CreateHandler(projectId, generator);

        await handler.ExecuteAsync(
            GenerateScriptTestData.CreateJob(
                projectId,
                GenerateScriptTestData.ValidPayload(projectId)),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;
        Assert.Contains("sound cues, visual cues, effects, or transitions", prompt);
        Assert.Contains("Translate their narrative intention into spoken words", prompt);
        Assert.Contains("instead of", prompt);
    }

    private static void AssertSpokenContract(string prompt)
    {
        Assert.Contains("Script fields are spoken content:", prompt);
        Assert.Contains("exact words intended to be spoken aloud", prompt);
        Assert.Contains("not necessarily spoken", prompt);
        Assert.Contains("camera, shot, lens, editing, acting, sound-effect, visual-effect", prompt);
        Assert.Contains("Those are CONTEXT to translate", prompt);
        Assert.Contains("sparkle effect", prompt);
        Assert.Contains("music", prompt);
        Assert.Contains("SFX", prompt);
        Assert.Contains("the room suddenly goes quiet", prompt);
    }

    private static GenerateScriptJobHandler CreateHandler(
        Guid projectId,
        IAiTextGenerator generator) =>
        new(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Test project", "Test brief")),
            generator,
            StoryContextTestSupport.Builder());

    private static string Payload(Guid projectId, CreativeDirection direction, StoryPlan plan) =>
        JsonSerializer.Serialize(
            new GenerateScriptJobPayload(
                projectId,
                GenerateScriptTestData.SelectedIdea,
                "Indonesian",
                direction,
                plan),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
