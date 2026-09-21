using System.Reflection;
using AIStudio.Application.Bibles;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Tests.Stories;
using AIStudio.Tests.StoryContext;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class StoryPlanGroundingTests
{
    private static readonly string[] StudentRef = ["student-01"];
    private static readonly string[] BedroomRef = ["student-bedroom"];

    [Fact]
    public void GroundsTheSameCharacterAndWorldAcrossMultipleBeats()
    {
        var plan = AnimePlan();
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var grounded = grounder.Ground(Request(
            plan,
            Grounding("beat-01", characters: StudentRef, worlds: BedroomRef),
            Grounding("beat-02", characters: StudentRef, worlds: BedroomRef),
            Grounding("beat-03", characters: StudentRef, worlds: BedroomRef)));

        Assert.All(grounded.Beats, beat => Assert.Equal(StudentRef, beat.CharacterRefs));
        Assert.All(grounded.Beats, beat => Assert.Equal(BedroomRef, beat.WorldRefs));
    }

    [Fact]
    public void PreservesOrderRolePurposeDurationAndContinuity()
    {
        var plan = AnimePlan();
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var grounded = grounder.Ground(Request(
            plan,
            Grounding("beat-02", characters: StudentRef, worlds: BedroomRef)));

        Assert.Equal(plan.Id, grounded.Id);
        Assert.Equal(plan.Version, grounded.Version);
        Assert.Equal(plan.SourceConceptId, grounded.SourceConceptId);
        Assert.Equal(plan.NarrativePattern, grounded.NarrativePattern);
        Assert.Equal(plan.NarrativePatternVersion, grounded.NarrativePatternVersion);
        Assert.Equal(plan.TargetDurationSeconds, grounded.TargetDurationSeconds);
        Assert.Equal(plan.Beats.Count, grounded.Beats.Count);

        for (var index = 0; index < plan.Beats.Count; index++)
        {
            Assert.Equal(plan.Beats[index].Id, grounded.Beats[index].Id);
            Assert.Equal(plan.Beats[index].Order, grounded.Beats[index].Order);
            Assert.Equal(plan.Beats[index].Role, grounded.Beats[index].Role);
            Assert.Equal(plan.Beats[index].Purpose, grounded.Beats[index].Purpose);
            Assert.Equal(plan.Beats[index].Importance, grounded.Beats[index].Importance);
            Assert.Equal(plan.Beats[index].TargetDurationSeconds, grounded.Beats[index].TargetDurationSeconds);
            Assert.Equal(plan.Beats[index].ContinuityFrom, grounded.Beats[index].ContinuityFrom);
        }
    }

    [Fact]
    public void PartialGroundingKeepsUntouchedBeatReferences()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 20, purpose: "setup", characterRefs: ["other-01"]),
                StoryTestSupport.Beat("beat-02", 2, role: "payoff", duration: 40, purpose: "resolve", continuityFrom: ["beat-01"])
            ],
            targetDuration: 60);
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var grounded = grounder.Ground(Request(
            plan,
            Grounding("beat-02", characters: StudentRef, worlds: BedroomRef)));

        Assert.Equal(new[] { "other-01" }, grounded.Beats[0].CharacterRefs.ToArray());
        Assert.Empty(grounded.Beats[0].WorldRefs);
        Assert.Equal(StudentRef, grounded.Beats[1].CharacterRefs);
        Assert.Equal(BedroomRef, grounded.Beats[1].WorldRefs);
    }

    [Fact]
    public void ExplicitEmptyReferencesAreAllowed()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60, purpose: "setup", characterRefs: ["student-01"], worldRefs: ["student-bedroom"])
            ],
            targetDuration: 60);
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var grounded = grounder.Ground(Request(plan, Grounding("beat-01")));

        Assert.Empty(grounded.Beats[0].CharacterRefs);
        Assert.Empty(grounded.Beats[0].WorldRefs);
    }

    [Fact]
    public void OriginalPlanIsNeverMutated()
    {
        var plan = AnimePlan();
        var originalCharacterRefs = plan.Beats[1].CharacterRefs;
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var grounded = grounder.Ground(Request(
            plan,
            Grounding("beat-02", characters: StudentRef, worlds: BedroomRef)));

        Assert.Empty(plan.Beats[1].CharacterRefs);
        Assert.Same(originalCharacterRefs, plan.Beats[1].CharacterRefs);
        Assert.Empty(plan.Beats[1].WorldRefs);
        Assert.NotSame(plan, grounded);
        Assert.NotSame(plan.Beats[1], grounded.Beats[1]);
    }

    [Fact]
    public void UnknownBeatIsRejected()
    {
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var exception = Assert.Throws<StoryPlanGroundingException>(
            () => grounder.Ground(Request(AnimePlan(), Grounding("beat-99", characters: StudentRef))));

        Assert.Equal(StoryPlanGroundingErrorCodes.BeatUnknown, exception.Code);
    }

    [Fact]
    public void DuplicateBeatAssignmentIsRejected()
    {
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var exception = Assert.Throws<StoryPlanGroundingException>(
            () => grounder.Ground(Request(
                AnimePlan(),
                Grounding("beat-02", characters: StudentRef),
                Grounding("beat-02", characters: ["other-01"]))));

        Assert.Equal(StoryPlanGroundingErrorCodes.BeatDuplicate, exception.Code);
    }

    [Fact]
    public void UnknownCharacterReferenceIsRejected()
    {
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var exception = Assert.Throws<StoryPlanGroundingException>(
            () => grounder.Ground(Request(AnimePlan(), Grounding("beat-02", characters: ["ghost-01"]))));

        Assert.Equal(StoryPlanGroundingErrorCodes.CharacterUnknown, exception.Code);
    }

    [Fact]
    public void UnknownWorldReferenceIsRejected()
    {
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var exception = Assert.Throws<StoryPlanGroundingException>(
            () => grounder.Ground(Request(AnimePlan(), Grounding("beat-02", worlds: ["ghost-world"]))));

        Assert.Equal(StoryPlanGroundingErrorCodes.WorldUnknown, exception.Code);
    }

    [Fact]
    public void MalformedIdentifierIsRejected()
    {
        var grounder = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds());

        var exception = Assert.Throws<StoryPlanGroundingException>(
            () => grounder.Ground(Request(AnimePlan(), Grounding("beat-02", characters: ["Student 01"]))));

        Assert.Equal(StoryPlanGroundingErrorCodes.CharacterUnknown, exception.Code);
    }

    [Fact]
    public void StoryContextResolvesGroundedReferencesAndExcludesUnrelated()
    {
        var grounded = GroundAnime();
        var context = new StoryContextBuilder(AnimeCharacters(), AnimeWorlds())
            .Build(new StoryContextRequest
            {
                CreativeDirection = StoryContextTestSupport.Direction(),
                StoryPlan = grounded
            })
            .Context;

        Assert.Equal(StudentRef, context.Characters.Select(character => character.Id.Value).ToArray());
        Assert.Equal(BedroomRef, context.Worlds.Select(world => world.Id.Value).ToArray());
        Assert.DoesNotContain(context.Characters, character => character.Id.Value == "other-01");
        Assert.DoesNotContain(context.Worlds, world => world.Id.Value == "other-world");
    }

    [Fact]
    public void BeatScopedStoryContextExcludesUnreachableReferences()
    {
        // student-bedroom is referenced by beat-01/02; attic only by the final beat.
        var grounded = new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds()).Ground(Request(
            AnimePlan(),
            Grounding("beat-01", characters: StudentRef, worlds: BedroomRef),
            Grounding("beat-02", characters: StudentRef, worlds: BedroomRef),
            Grounding("beat-03", characters: StudentRef, worlds: ["attic"])));

        var context = new StoryContextBuilder(AnimeCharacters(), AnimeWorlds())
            .Build(new StoryContextRequest
            {
                CreativeDirection = StoryContextTestSupport.Direction(),
                StoryPlan = grounded,
                BeatId = new StoryBeatId("beat-02")
            })
            .Context;

        Assert.Equal(BedroomRef, context.Worlds.Select(world => world.Id.Value).ToArray());
        Assert.DoesNotContain(context.Worlds, world => world.Id.Value == "attic");
    }

    [Fact]
    public void CharacterAndWorldStateDoNotMutateIdentity()
    {
        var characters = AnimeCharacters();
        var worlds = AnimeWorlds();
        var before = characters.GetLatest(new CharacterBibleId("student-01"));
        var worldBefore = worlds.GetLatest(new WorldBibleId("student-bedroom"));
        var grounded = new StoryPlanGrounder(characters, worlds).Ground(Request(
            AnimePlan(),
            Grounding("beat-01", characters: StudentRef, worlds: BedroomRef)));

        var result = new StoryContextBuilder(characters, worlds).Build(new StoryContextRequest
        {
            CreativeDirection = StoryContextTestSupport.Direction(),
            StoryPlan = grounded,
            CharacterStates = [BibleTestSupport.CharacterState("student-01", emotion: "uneasy", action: "notices the anomaly")],
            WorldStates = [BibleTestSupport.WorldState("student-bedroom", lighting: "dim")]
        });

        Assert.NotNull(result.Context.States);
        Assert.Contains(result.Context.States!.Characters, state => state.Emotion == "uneasy");
        Assert.Contains(result.Context.States.Worlds, state => state.Lighting == "dim");

        var after = characters.GetLatest(new CharacterBibleId("student-01"));
        var worldAfter = worlds.GetLatest(new WorldBibleId("student-bedroom"));
        Assert.Same(before, after);
        Assert.Same(worldBefore, worldAfter);
        Assert.Equal(before.Identity, after.Identity);
        Assert.Equal(worldBefore.Identity, worldAfter.Identity);
    }

    [Fact]
    public void AnimeAndPreschoolUseTheSameGroundingMechanism()
    {
        var anime = GroundAnime();

        var preschoolCharacters = new CharacterBibleRegistry(
            [BibleTestSupport.Character("child-01", role: "protagonist", species: "human")]);
        var preschoolWorlds = new WorldBibleRegistry(
            [BibleTestSupport.World("playroom", environmentType: "playroom", recurringProps: ["blocks", "shelf"])]);
        var preschoolPlan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 15, purpose: "play"),
                StoryTestSupport.Beat("beat-02", 2, role: "payoff", duration: 30, purpose: "tidy", continuityFrom: ["beat-01"])
            ],
            sourceConceptId: "toy-putaway-chain",
            targetDuration: 45);

        var preschool = new StoryPlanGrounder(preschoolCharacters, preschoolWorlds).Ground(Request(
            preschoolPlan,
            Grounding("beat-01", characters: ["child-01"], worlds: ["playroom"]),
            Grounding("beat-02", characters: ["child-01"], worlds: ["playroom"])));

        Assert.Equal(StudentRef, anime.Beats[0].CharacterRefs);
        Assert.All(preschool.Beats, beat => Assert.Equal(new[] { "child-01" }, beat.CharacterRefs.ToArray()));
        Assert.All(preschool.Beats, beat => Assert.Equal(new[] { "playroom" }, beat.WorldRefs.ToArray()));
    }

    [Fact]
    public void PlanWithEmptyReferencesRemainsValidWithoutAssignments()
    {
        var plan = AnimePlan();
        var grounder = new StoryPlanGrounder(new CharacterBibleRegistry(), new WorldBibleRegistry());

        var grounded = grounder.Ground(Request(plan));

        Assert.All(grounded.Beats, beat => Assert.Empty(beat.CharacterRefs));
        Assert.All(grounded.Beats, beat => Assert.Empty(beat.WorldRefs));
        Assert.Equal(plan.Beats.Count, grounded.Beats.Count);
    }

    [Fact]
    public void GroundingContractsCarryNoRuntimeDetail()
    {
        var names = typeof(StoryBeatGrounding).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Concat(typeof(StoryPlanGroundingRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.Name)
            .ToList();

        Assert.Contains("BeatId", names);
        Assert.Contains("CharacterRefs", names);
        Assert.Contains("WorldRefs", names);
        Assert.Contains("StoryPlan", names);
        Assert.Contains("Beats", names);

        foreach (var forbidden in new[] { "Path", "Url", "Provider", "Command", "Model", "Executable" })
        {
            Assert.DoesNotContain(names, name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static StoryPlan GroundAnime() =>
        new StoryPlanGrounder(AnimeCharacters(), AnimeWorlds()).Ground(Request(
            AnimePlan(),
            Grounding("beat-01", characters: StudentRef, worlds: BedroomRef),
            Grounding("beat-02", characters: StudentRef, worlds: BedroomRef),
            Grounding("beat-03", characters: StudentRef, worlds: BedroomRef)));

    private static StoryPlanGroundingRequest Request(
        StoryPlan plan,
        params StoryBeatGrounding[] beats) =>
        new() { StoryPlan = plan, Beats = beats };

    private static StoryBeatGrounding Grounding(
        string beatId,
        string[]? characters = null,
        string[]? worlds = null) =>
        new()
        {
            BeatId = new StoryBeatId(beatId),
            CharacterRefs = characters ?? [],
            WorldRefs = worlds ?? []
        };

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

    private static CharacterBibleRegistry AnimeCharacters() =>
        new(
        [
            BibleTestSupport.Character("student-01", role: "protagonist", species: "human", hair: "black", eyes: "brown", personalityTraits: ["reserved"]),
            BibleTestSupport.Character("other-01")
        ]);

    private static WorldBibleRegistry AnimeWorlds() =>
        new(
        [
            BibleTestSupport.World("student-bedroom", environmentType: "bedroom", recurringProps: ["desk", "laptop"]),
            BibleTestSupport.World("attic", environmentType: "attic"),
            BibleTestSupport.World("other-world")
        ]);
}
