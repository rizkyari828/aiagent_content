using System.Reflection;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Bibles;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Tests.StoryContext;
using AIStudio.Tests.Stories;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class QwenStoryBiblePlannerTests
{
    [Fact]
    public async Task BuildsValidatedProposalFromModelJson()
    {
        var planner = Planner(AnimeResponse());

        var proposal = await planner.BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken);

        Assert.Single(proposal.CharacterBibles);
        Assert.Equal("student-01", proposal.CharacterBibles[0].Id.Value);
        Assert.Single(proposal.WorldBibles);
        Assert.Equal("student-bedroom", proposal.WorldBibles[0].Id.Value);
        Assert.Equal(3, proposal.BeatGroundings.Count);
    }

    [Fact]
    public async Task ReusesRecurringCharacterIdAcrossBeats()
    {
        var proposal = await Planner(AnimeResponse())
            .BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken);

        Assert.All(proposal.BeatGroundings, grounding => Assert.Equal(new[] { "student-01" }, grounding.CharacterRefs.ToArray()));
    }

    [Fact]
    public async Task ReusesRecurringWorldIdAcrossBeats()
    {
        var proposal = await Planner(AnimeResponse())
            .BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken);

        Assert.All(proposal.BeatGroundings, grounding => Assert.Equal(new[] { "student-bedroom" }, grounding.WorldRefs.ToArray()));
    }

    [Fact]
    public async Task InvalidJsonFailsClearly()
    {
        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner("{ not json").BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public async Task AdditionalPropertiesAreRejected()
    {
        var json = AnimeResponse().Replace("{\"characterBibles\"", "{\"surprise\":true,\"characterBibles\"", StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(json).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public async Task MalformedCharacterIdIsRejected()
    {
        var json = AnimeResponse().Replace("\"student-01\"", "\"student 01\"", StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(json).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public async Task MalformedWorldIdIsRejected()
    {
        var json = AnimeResponse().Replace("\"student-bedroom\"", "\"student bedroom\"", StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(json).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public async Task DuplicateCharacterIdIsRejected()
    {
        var response = Response(
            [AnimeCharacter(), AnimeCharacter()],
            [AnimeWorld()],
            Groundings());

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(response).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.DuplicateCharacter, exception.Code);
    }

    [Fact]
    public async Task DuplicateWorldIdIsRejected()
    {
        var response = Response(
            [AnimeCharacter()],
            [AnimeWorld(), AnimeWorld()],
            Groundings());

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(response).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.DuplicateWorld, exception.Code);
    }

    [Fact]
    public async Task UnknownBeatGroundingIsRejected()
    {
        var response = Response(
            [AnimeCharacter()],
            [AnimeWorld()],
            [Grounding("beat-99", "student-01", "student-bedroom")]);

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(response).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.BeatUnknown, exception.Code);
    }

    [Fact]
    public async Task DuplicateBeatGroundingIsRejected()
    {
        var response = Response(
            [AnimeCharacter()],
            [AnimeWorld()],
            [Grounding("beat-01", "student-01", "student-bedroom"), Grounding("beat-01", "student-01", "student-bedroom")]);

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(response).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.BeatDuplicate, exception.Code);
    }

    [Fact]
    public async Task GroundingCharacterRefNotInProposalIsRejected()
    {
        var response = Response(
            [AnimeCharacter()],
            [AnimeWorld()],
            [Grounding("beat-01", "ghost-01", "student-bedroom")]);

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(response).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.CharacterRefUnknown, exception.Code);
    }

    [Fact]
    public async Task GroundingWorldRefNotInProposalIsRejected()
    {
        var response = Response(
            [AnimeCharacter()],
            [AnimeWorld()],
            [Grounding("beat-01", "student-01", "ghost-world")]);

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(response).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.WorldRefUnknown, exception.Code);
    }

    [Fact]
    public async Task RelationshipToUnknownCharacterIsRejected()
    {
        var character = BibleTestSupport.Character(
            "student-01",
            role: "protagonist",
            relationships: [BibleTestSupport.Relationship("ghost-01", "friend")]);
        var response = Response([character], [AnimeWorld()], Groundings());

        var exception = await Assert.ThrowsAsync<StoryBiblePlanningException>(
            () => Planner(response).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken));

        Assert.Equal(StoryBiblePlanningErrorCodes.RelationshipUnknown, exception.Code);
    }

    [Fact]
    public async Task PreschoolFixtureUsesTheSameGenericContracts()
    {
        var plan = PreschoolPlan();
        var response = Response(
            [BibleTestSupport.Character("child-01", role: "protagonist", species: "human")],
            [BibleTestSupport.World("playroom", environmentType: "playroom", recurringProps: ["blocks", "shelf"])],
            [Grounding("beat-01", "child-01", "playroom"), Grounding("beat-02", "child-01", "playroom")]);

        var proposal = await Planner(response).BuildAsync(Request(plan), TestContext.Current.CancellationToken);

        Assert.Equal("child-01", proposal.CharacterBibles[0].Id.Value);
        Assert.Equal("playroom", proposal.WorldBibles[0].Id.Value);
        Assert.All(proposal.BeatGroundings, grounding => Assert.Equal(new[] { "child-01" }, grounding.CharacterRefs.ToArray()));
    }

    [Fact]
    public async Task OneBuildCallMakesOneAiRequest()
    {
        var generator = new StubAiTextGenerator(AnimeResponse());

        await new QwenStoryBiblePlanner(generator).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken);

        Assert.Single(generator.Requests);
    }

    [Fact]
    public async Task CancellationTokenFlowsToAiTextGenerator()
    {
        var generator = new StubAiTextGenerator(AnimeResponse());
        using var cancellation = new CancellationTokenSource();

        await new QwenStoryBiblePlanner(generator).BuildAsync(Request(AnimePlan()), cancellation.Token);

        Assert.Equal(cancellation.Token, generator.LastToken);
    }

    [Fact]
    public async Task PlannerHasNoRegistrationOrPlanMutationSideEffects()
    {
        var plan = AnimePlan();
        var characters = new CharacterBibleRegistry();
        var worlds = new WorldBibleRegistry();

        await Planner(AnimeResponse()).BuildAsync(Request(plan), TestContext.Current.CancellationToken);

        Assert.All(plan.Beats, beat => Assert.Empty(beat.CharacterRefs));
        Assert.All(plan.Beats, beat => Assert.Empty(beat.WorldRefs));
        Assert.Empty(characters.Characters);
        Assert.Empty(worlds.Worlds);

        var parameters = typeof(QwenStoryBiblePlanner)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();
        Assert.Equal(new[] { typeof(IAiTextGenerator) }, parameters);
    }

    [Fact]
    public async Task ProposalCanBeRegisteredGroundAndProjectedThroughExistingContracts()
    {
        var direction = StoryContextTestSupport.Direction();
        var plan = AnimePlan();
        var proposal = await Planner(AnimeResponse()).BuildAsync(Request(plan), TestContext.Current.CancellationToken);

        // Explicit registration, then the deterministic grounder, then StoryContext.
        var characters = new CharacterBibleRegistry(proposal.CharacterBibles);
        var worlds = new WorldBibleRegistry(proposal.WorldBibles);
        var grounded = new StoryPlanGrounder(characters, worlds).Ground(
            new StoryPlanGroundingRequest { StoryPlan = plan, Beats = proposal.BeatGroundings });

        var context = new StoryContextBuilder(characters, worlds).Build(new StoryContextRequest
        {
            CreativeDirection = direction,
            StoryPlan = grounded
        }).Context;

        Assert.Equal(new[] { "student-01" }, context.Characters.Select(character => character.Id.Value).ToArray());
        Assert.Equal(new[] { "student-bedroom" }, context.Worlds.Select(world => world.Id.Value).ToArray());
        Assert.All(grounded.Beats, beat => Assert.Equal(new[] { "student-bedroom" }, beat.WorldRefs.ToArray()));
    }

    [Fact]
    public async Task PromptDistinguishesStableIdentityFromMutableState()
    {
        var prompt = await PromptAsync();

        Assert.Contains("stable identity only", prompt);
        Assert.Contains("temporary scene state", prompt);
        Assert.Contains("stable environment identity only", prompt);
        Assert.Contains("Never put temporary", prompt);
    }

    [Fact]
    public async Task PromptRequiresExactIdReuseWithoutVersionSuffix()
    {
        var prompt = await PromptAsync();

        Assert.Contains("reuse that exact same id token", prompt);
        Assert.Contains("never include a version suffix", prompt);
        Assert.Contains("student-01", prompt);
    }

    [Fact]
    public async Task PromptPreservesStoryPlanAuthorityAndForbidsLeakage()
    {
        var prompt = await PromptAsync();

        Assert.Contains("do not change their ids, order, role, purpose, or duration", prompt);
        Assert.Contains("empty array", prompt);
        Assert.Contains("provider, model, engine, filesystem path, URL, or command", prompt);
        Assert.Contains("Do not write dialogue", prompt);
        Assert.Contains("Do not give camera, shot, or storyboard directions", prompt);
        Assert.Contains("do not add extra properties", prompt);
        Assert.Contains("markdown fences", prompt);
    }

    [Fact]
    public void RelationshipAsBareCharacterIdIsRejected()
    {
        // Real anime run evidence: the model emitted relationships as ["student-01"].
        var json = AnimeResponse().Replace(
            "\"relationships\":[]",
            "\"relationships\":[\"student-01\"]",
            StringComparison.Ordinal);

        var exception = Assert.Throws<StoryBiblePlanningException>(
            () => StoryBiblePlanParser.Parse(json, AnimePlan()));

        Assert.Equal(StoryBiblePlanningErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void RelationshipObjectShapeParses()
    {
        var json = Response(
            [
                BibleTestSupport.Character(
                    "student-01",
                    role: "protagonist",
                    relationships: [BibleTestSupport.Relationship("classmate-01", "classmate")]),
                BibleTestSupport.Character("classmate-01", role: "supporting")
            ],
            [AnimeWorld()],
            Groundings());

        var proposal = StoryBiblePlanParser.Parse(json, AnimePlan());

        Assert.Equal(2, proposal.CharacterBibles.Count);
        Assert.Equal("classmate-01", proposal.CharacterBibles[0].Relationships[0].Target.Value);
    }

    [Fact]
    public void CapitalizedRoleTokenIsRejectedByValidator()
    {
        // Real preschool run evidence: the model emitted role "Protagonist".
        var issues = CharacterBibleValidator.Validate(
            BibleTestSupport.Character("child-01", role: "Protagonist"));

        Assert.Contains(issues, issue => issue.Code == BibleIssueCodes.CharacterRoleInvalid);
    }

    [Fact]
    public void NormalizedRoleTokenIsAcceptedByValidator()
    {
        var issues = CharacterBibleValidator.Validate(
            BibleTestSupport.Character("child-01", role: "protagonist"));

        Assert.DoesNotContain(issues, issue => issue.Code == BibleIssueCodes.CharacterRoleInvalid);
    }

    [Fact]
    public void OutputContractExampleParsesUnderStrictContracts()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 5, purpose: "setup"),
                StoryTestSupport.Beat("beat-02", 2, role: "payoff", duration: 5, purpose: "resolution", continuityFrom: ["beat-01"])
            ],
            sourceConceptId: "example-concept",
            targetDuration: 10);

        var proposal = StoryBiblePlanParser.Parse(QwenStoryBiblePlannerPrompt.OutputContract, plan);

        Assert.Equal(2, proposal.CharacterBibles.Count);
        Assert.Single(proposal.WorldBibles);
        Assert.Equal(2, proposal.BeatGroundings.Count);
    }

    [Fact]
    public async Task PromptExplainsTokenVersusProseFields()
    {
        var prompt = await PromptAsync();

        Assert.Contains("TOKEN fields", prompt);
        Assert.Contains("PROSE fields", prompt);
        Assert.Contains("identity.role", prompt);
        Assert.Contains("identity.visualDescription", prompt);
        Assert.Contains("lists of objects, never lists of bare strings", prompt);
    }

    [Fact]
    public async Task PromptShowsNormalizedTokenExamplesForRoleAndEnvironment()
    {
        var prompt = await PromptAsync();

        Assert.Contains("role \"protagonist\"", prompt);
        Assert.Contains("never \"Main Protagonist of the mystery\"", prompt);
        Assert.Contains("environmentType \"bedroom\"", prompt);
        Assert.Contains("never \"small dim bedroom where the student studies\"", prompt);
    }

    [Fact]
    public async Task PromptKeepsStableIdReuseAndLeakageGuards()
    {
        var prompt = await PromptAsync();

        Assert.Contains("reuse that exact same id token", prompt);
        Assert.Contains("Never create several ids", prompt);
        Assert.Contains("empty array", prompt);
        Assert.Contains("provider, model, engine, filesystem path, URL, or command", prompt);
        Assert.Contains("do not add extra properties", prompt);
    }

    [Fact]
    public async Task PromptRequiresMutuallyConsistentIdentityFields()
    {
        var prompt = await PromptAsync();

        Assert.Contains("must describe the SAME identity", prompt);
        Assert.Contains("must not contradict each other", prompt);
        Assert.Contains("environment type", prompt);
    }

    [Fact]
    public async Task PromptDistinguishesCharactersFromProps()
    {
        var prompt = await PromptAsync();

        Assert.Contains("persistent narrative character or agent", prompt);
        Assert.Contains("must not become characterBibles", prompt);
        Assert.Contains("worldBibles.recurringProps", prompt);
        Assert.Contains("agency, not species, decides", prompt);
    }

    [Fact]
    public async Task PromptForbidsInventingUnsupportedWorldClassification()
    {
        var prompt = await PromptAsync();

        Assert.Contains("Never invent a more specific environment classification", prompt);
        Assert.Contains("closest supported neutral environment type", prompt);
        Assert.Contains("reuse that exact same id token", prompt);
    }

    private static async Task<string> PromptAsync()
    {
        var generator = new StubAiTextGenerator(AnimeResponse());
        await new QwenStoryBiblePlanner(generator).BuildAsync(Request(AnimePlan()), TestContext.Current.CancellationToken);
        return generator.Requests.Single().Prompt;
    }

    private static QwenStoryBiblePlanner Planner(string response) =>
        new(new StubAiTextGenerator(response));

    private static StoryBiblePlanningRequest Request(StoryPlan plan) =>
        new() { CreativeDirection = StoryContextTestSupport.Direction(), StoryPlan = plan };

    private static string AnimeResponse() =>
        Response([AnimeCharacter()], [AnimeWorld()], Groundings());

    private static CharacterBible AnimeCharacter() =>
        BibleTestSupport.Character(
            "student-01",
            role: "protagonist",
            species: "human",
            hair: "black",
            eyes: "brown",
            personalityTraits: ["reserved"]);

    private static WorldBible AnimeWorld() =>
        BibleTestSupport.World(
            "student-bedroom",
            environmentType: "bedroom",
            visualDescription: "a small dim bedroom with a desk and a laptop",
            recurringProps: ["desk", "laptop"]);

    private static IReadOnlyList<StoryBeatGrounding> Groundings() =>
    [
        Grounding("beat-01", "student-01", "student-bedroom"),
        Grounding("beat-02", "student-01", "student-bedroom"),
        Grounding("beat-03", "student-01", "student-bedroom")
    ];

    private static StoryBeatGrounding Grounding(string beatId, string characterRef, string worldRef) =>
        new()
        {
            BeatId = new StoryBeatId(beatId),
            CharacterRefs = [characterRef],
            WorldRefs = [worldRef]
        };

    private static string Response(
        IReadOnlyList<CharacterBible> characters,
        IReadOnlyList<WorldBible> worlds,
        IReadOnlyList<StoryBeatGrounding> groundings) =>
        JsonSerializer.Serialize(
            new { characterBibles = characters, worldBibles = worlds, beatGroundings = groundings },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static StoryPlan AnimePlan() =>
        StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 10, purpose: "calm setup"),
                StoryTestSupport.Beat("beat-02", 2, role: "problem", duration: 20, purpose: "the anomaly escalates", continuityFrom: ["beat-01"]),
                StoryTestSupport.Beat("beat-03", 3, role: "payoff", duration: 30, purpose: "the reveal", continuityFrom: ["beat-02"])
            ],
            id: "ai-phantom-memory-story",
            sourceConceptId: "ai-phantom-memory",
            targetDuration: 60);

    private static StoryPlan PreschoolPlan() =>
        StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 15, purpose: "play"),
                StoryTestSupport.Beat("beat-02", 2, role: "payoff", duration: 30, purpose: "tidy", continuityFrom: ["beat-01"])
            ],
            sourceConceptId: "toy-putaway-chain",
            targetDuration: 45);

    private sealed class StubAiTextGenerator(string response) : IAiTextGenerator
    {
        public List<AiTextRequest> Requests { get; } = [];

        public CancellationToken LastToken { get; private set; }

        public Task<AiTextResponse> GenerateAsync(
            AiTextRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            LastToken = cancellationToken;
            return Task.FromResult(new AiTextResponse(response, "fake-model", null, null, null));
        }
    }
}
