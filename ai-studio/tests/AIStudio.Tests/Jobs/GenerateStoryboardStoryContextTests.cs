using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.Creative;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Domain.Scripts;
using AIStudio.Tests.Stories;
using AIStudio.Tests.StoryContext;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateStoryboardStoryContextTests
{
    [Fact]
    public async Task Handler_ProjectsCompactStoryContextIntoTheStoryboardPrompt()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = CreateHandler(projectId, generator, StoryContextTestSupport.Builder());

        await handler.ExecuteAsync(
            GenerateStoryboardTestData.CreateJob(
                projectId,
                Payload(projectId, StoryContextTestSupport.Direction(), TwoBeatPlan())),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;

        Assert.Contains("Narrative planning context", prompt);
        Assert.Contains("Audience: developers", prompt);
        Assert.Contains("beat-01", prompt);
        Assert.Contains("hook purpose", prompt);
        Assert.Contains("beat-02", prompt);
        Assert.Contains("payoff purpose", prompt);
        // Spoken script is still present and unchanged as the reference.
        Assert.Contains("Script title: Local AI Tutorial", prompt);
        Assert.Contains("Section 1 heading: Why local AI", prompt);
        Assert.Contains("Section 1 narration:", prompt);

        Assert.True(
            prompt.IndexOf("beat-01", StringComparison.Ordinal)
            < prompt.IndexOf("beat-02", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handler_AlignsScriptSectionsWithStoryPlanBeats()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = CreateHandler(projectId, generator, StoryContextTestSupport.Builder());

        await handler.ExecuteAsync(
            GenerateStoryboardTestData.CreateJob(
                projectId,
                Payload(projectId, StoryContextTestSupport.Direction(), TwoBeatPlan())),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;

        Assert.Contains("one section per beat in the same order", prompt);
        Assert.True(
            prompt.IndexOf("Section 1 heading", StringComparison.Ordinal)
            < prompt.IndexOf("Section 2 heading", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handler_PassesTheAuthoritativePlanAndDirectionToTheContextBuilder()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var spy = new RecordingContextBuilder(StoryContextTestSupport.Builder());
        var handler = CreateHandler(projectId, generator, spy);

        var plan = TwoBeatPlan();
        var direction = StoryContextTestSupport.Direction(conceptId: "ai-phantom-memory");

        await handler.ExecuteAsync(
            GenerateStoryboardTestData.CreateJob(projectId, Payload(projectId, direction, plan)),
            TestContext.Current.CancellationToken);

        Assert.NotNull(spy.Request);
        Assert.Equal("ai-phantom-memory", spy.Request!.CreativeDirection.Concept.Id.Value);
        Assert.Equal(plan.Id.Value, spy.Request.StoryPlan.Id.Value);
        Assert.Equal(plan.Beats.Count, spy.Request.StoryPlan.Beats.Count);
    }

    [Fact]
    public async Task Handler_IncludesOnlyReferencedCharacterAndWorldContext()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = CreateHandler(projectId, generator, StoryContextTestSupport.Builder());

        await handler.ExecuteAsync(
            GenerateStoryboardTestData.CreateJob(
                projectId,
                Payload(projectId, StoryContextTestSupport.Direction(), TwoBeatPlan())),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;
        Assert.Contains("rio", prompt);
        Assert.Contains("bedroom", prompt);
        Assert.Contains("office", prompt);
        Assert.DoesNotContain("alex", prompt);
        Assert.DoesNotContain("spaceship", prompt);
    }

    [Fact]
    public async Task Handler_ToleratesEmptyCharacterAndWorldReferences()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = CreateHandler(projectId, generator, StoryContextTestSupport.Builder());
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 20, purpose: "bare hook"),
                StoryTestSupport.Beat("beat-02", 2, role: "payoff", duration: 40, purpose: "bare payoff", continuityFrom: ["beat-01"])
            ],
            targetDuration: 60);

        await handler.ExecuteAsync(
            GenerateStoryboardTestData.CreateJob(
                projectId,
                Payload(projectId, StoryContextTestSupport.Direction(), plan)),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;
        Assert.Contains("bare payoff", prompt);
        Assert.DoesNotContain("Characters (stable identity", prompt);
        Assert.DoesNotContain("Worlds (stable identity", prompt);
    }

    [Fact]
    public async Task Handler_ProjectsOptionalStateWithoutMutatingIdentity()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var characters = StoryContextTestSupport.Characters();
        var before = characters.GetLatest(new CharacterBibleId("rio"));
        var handler = CreateHandler(projectId, generator, StoryContextTestSupport.Builder(characters));

        await handler.ExecuteAsync(
            GenerateStoryboardTestData.CreateJob(
                projectId,
                Payload(
                    projectId,
                    StoryContextTestSupport.Direction(),
                    TwoBeatPlan(),
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
    public async Task Prompt_AllowsVisualLanguageAndForbidsImplementationDetail()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = CreateHandler(projectId, generator, StoryContextTestSupport.Builder());

        await handler.ExecuteAsync(
            GenerateStoryboardTestData.CreateJob(
                projectId,
                Payload(projectId, StoryContextTestSupport.Direction(), TwoBeatPlan())),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;
        Assert.Contains("framing, composition, camera movement, and transition intent are all allowed", prompt);
        Assert.Contains("engine-neutral and implementation-free", prompt);
        Assert.Contains("providers, models, node graphs, render scripts, executables, file paths, URLs, or commands", prompt);
        Assert.Contains("do not rewrite, replace, or add dialogue or narration", prompt);
    }

    [Fact]
    public async Task Handler_KeepsExistingBehaviorWhenPayloadHasNoNarrativeContext()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = CreateHandler(projectId, generator, StoryContextTestSupport.Builder());

        await handler.ExecuteAsync(
            GenerateStoryboardTestData.CreateJob(
                projectId,
                GenerateStoryboardTestData.ValidPayload(projectId)),
            TestContext.Current.CancellationToken);

        var prompt = generator.Request!.Prompt;
        Assert.DoesNotContain("Narrative planning context", prompt);
        Assert.Contains("Script title: Local AI Tutorial", prompt);
        Assert.Contains("heading, visual", prompt);
    }

    private static StoryPlan TwoBeatPlan() =>
        StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat(
                    "beat-01",
                    1,
                    role: "hook",
                    duration: 20,
                    purpose: "hook purpose",
                    characterRefs: ["rio"],
                    worldRefs: ["bedroom"]),
                StoryTestSupport.Beat(
                    "beat-02",
                    2,
                    role: "payoff",
                    duration: 40,
                    purpose: "payoff purpose",
                    continuityFrom: ["beat-01"],
                    characterRefs: ["rio"],
                    worldRefs: ["office"])
            ],
            targetDuration: 60);

    private static GenerateStoryboardJobHandler CreateHandler(
        Guid projectId,
        IAiTextGenerator generator,
        IStoryContextBuilder storyContextBuilder) =>
        new(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Test project", "Test brief")),
            new StubScriptReviewRepository(
                GenerateStoryboardTestData.Script(projectId, ScriptReviewStatus.Approved)),
            generator,
            storyContextBuilder);

    private static string Payload(
        Guid projectId,
        CreativeDirection direction,
        StoryPlan plan,
        IReadOnlyList<CharacterState>? characterStates = null,
        IReadOnlyList<WorldState>? worldStates = null) =>
        JsonSerializer.Serialize(
            new GenerateStoryboardJobPayload(
                projectId,
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
