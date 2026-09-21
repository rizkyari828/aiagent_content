using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.Creative;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Tests.Stories;
using AIStudio.Tests.StoryContext;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateScriptStoryContextTests
{
    [Fact]
    public async Task Handler_ProjectsCompactStoryContextIntoTheScriptPrompt()
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
        Assert.Contains("Audience: developers", prompt);
        Assert.Contains("beat-01", prompt);
        Assert.Contains("hook purpose", prompt);
        Assert.Contains("problem purpose", prompt);
        Assert.Contains("payoff purpose", prompt);
        Assert.Contains("one section per beat", prompt);

        Assert.True(
            prompt.IndexOf("beat-01", StringComparison.Ordinal)
            < prompt.IndexOf("beat-02", StringComparison.Ordinal));
        Assert.True(
            prompt.IndexOf("beat-02", StringComparison.Ordinal)
            < prompt.IndexOf("beat-03", StringComparison.Ordinal));

        // Only referenced characters/worlds are projected; unrelated bible entries stay out.
        Assert.Contains("rio", prompt);
        Assert.Contains("hana", prompt);
        Assert.Contains("bedroom", prompt);
        Assert.Contains("office", prompt);
        Assert.DoesNotContain("alex", prompt);
        Assert.DoesNotContain("spaceship", prompt);

        // Script owns exact spoken words; storyboard/runtime detail stays out.
        Assert.Contains("Script owns the exact spoken words", prompt);
        Assert.Contains("camera or shot instructions", prompt);
        Assert.Contains("provider or model names", prompt);
        Assert.Contains("filesystem paths", prompt);
    }

    [Fact]
    public async Task Handler_ProjectsOptionalStateWithoutMutatingIdentity()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateScriptTestData.ValidResult);
        var characters = StoryContextTestSupport.Characters();
        var before = characters.GetLatest(new CharacterBibleId("rio"));
        var handler = new GenerateScriptJobHandler(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Test project", "Test brief")),
            generator,
            StoryContextTestSupport.Builder(characters));

        await handler.ExecuteAsync(
            GenerateScriptTestData.CreateJob(
                projectId,
                Payload(
                    projectId,
                    StoryContextTestSupport.Direction(),
                    StoryContextTestSupport.ThreeBeatPlan(),
                    characterStates:
                    [
                        new CharacterState
                        {
                            CharacterRef = new CharacterBibleId("rio"),
                            Emotion = "determined",
                            Action = "notices the anomaly"
                        }
                    ])),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;
        Assert.Contains("Current state", prompt);
        Assert.Contains("emotion=determined", prompt);
        Assert.Contains("action=notices the anomaly", prompt);
        Assert.Contains("never treat it as identity", prompt);

        var after = characters.GetLatest(new CharacterBibleId("rio"));
        Assert.Same(before, after);
        Assert.Equal(before.Identity, after.Identity);
    }

    [Fact]
    public async Task Handler_PassesTheAuthoritativePlanAndDirectionToTheContextBuilder()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateScriptTestData.ValidResult);
        var spy = new RecordingContextBuilder(StoryContextTestSupport.Builder());
        var handler = new GenerateScriptJobHandler(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Test project", "Test brief")),
            generator,
            spy);

        var plan = StoryContextTestSupport.ThreeBeatPlan();
        var direction = StoryContextTestSupport.Direction(conceptId: "ai-phantom-memory");

        await handler.ExecuteAsync(
            GenerateScriptTestData.CreateJob(projectId, Payload(projectId, direction, plan)),
            TestContext.Current.CancellationToken);

        Assert.NotNull(spy.Request);
        Assert.Equal("ai-phantom-memory", spy.Request!.CreativeDirection.Concept.Id.Value);
        Assert.Equal(plan.Id.Value, spy.Request.StoryPlan.Id.Value);
        Assert.Equal(plan.Beats.Count, spy.Request.StoryPlan.Beats.Count);
    }

    [Fact]
    public async Task Handler_ToleratesEmptyCharacterAndWorldReferences()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateScriptTestData.ValidResult);
        var handler = CreateHandler(projectId, generator);
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat(
                    "beat-01",
                    1,
                    role: "hook",
                    duration: 45,
                    purpose: "a single uncomplicated beat")
            ],
            targetDuration: 45);

        await handler.ExecuteAsync(
            GenerateScriptTestData.CreateJob(
                projectId,
                Payload(projectId, StoryContextTestSupport.Direction(), plan)),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;
        Assert.Contains("a single uncomplicated beat", prompt);
        Assert.DoesNotContain("Characters (identity", prompt);
        Assert.DoesNotContain("Worlds (identity", prompt);
    }

    [Fact]
    public async Task Handler_KeepsExistingBehaviorWhenPayloadHasNoNarrativeContext()
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
        Assert.DoesNotContain("Narrative planning context", prompt);
        Assert.Contains("Idea title: Local AI Content", prompt);
        Assert.Contains("Response language: Indonesian", prompt);
    }

    private static GenerateScriptJobHandler CreateHandler(
        Guid projectId,
        IAiTextGenerator generator) =>
        new(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Test project", "Test brief")),
            generator,
            StoryContextTestSupport.Builder());

    private static string Payload(
        Guid projectId,
        CreativeDirection direction,
        StoryPlan plan,
        IReadOnlyList<CharacterState>? characterStates = null,
        IReadOnlyList<WorldState>? worldStates = null) =>
        JsonSerializer.Serialize(
            new GenerateScriptJobPayload(
                projectId,
                GenerateScriptTestData.SelectedIdea,
                "Indonesian",
                direction,
                plan,
                characterStates,
                worldStates),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed class RecordingContextBuilder(IStoryContextBuilder inner) : IStoryContextBuilder
    {
        public StoryContextRequest? Request { get; private set; }

        public StoryContextBuildResult Build(StoryContextRequest request)
        {
            Request = request;
            return inner.Build(request);
        }
    }
}
