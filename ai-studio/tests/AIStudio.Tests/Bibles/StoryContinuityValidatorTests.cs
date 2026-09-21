using AIStudio.Application.Bibles;
using AIStudio.Tests.Stories;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class StoryContinuityValidatorTests
{
    [Fact]
    public void KnownCharacterAndWorldReferencesPass()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60, characterRefs: ["rio"], worldRefs: ["rio-bedroom"])
            ],
            targetDuration: 60);

        var characters = new CharacterBibleRegistry([BibleTestSupport.Character("rio")]);
        var worlds = new WorldBibleRegistry([BibleTestSupport.World("rio-bedroom")]);

        Assert.Empty(StoryContinuityValidator.Validate(plan, characters, worlds));
    }

    [Fact]
    public void UnknownCharacterReferenceIsReported()
    {
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60, characterRefs: ["ghost"])],
            targetDuration: 60);

        var issues = StoryContinuityValidator.Validate(
            plan,
            new CharacterBibleRegistry(),
            new WorldBibleRegistry());

        Assert.Contains(BibleIssueCodes.StoryCharacterReferenceUnknown, issues.Select(issue => issue.Code));
    }

    [Fact]
    public void UnknownWorldReferenceIsReported()
    {
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60, worldRefs: ["nowhere"])],
            targetDuration: 60);

        var issues = StoryContinuityValidator.Validate(
            plan,
            new CharacterBibleRegistry(),
            new WorldBibleRegistry());

        Assert.Contains(BibleIssueCodes.StoryWorldReferenceUnknown, issues.Select(issue => issue.Code));
    }

    [Fact]
    public void RepeatedUnknownReferenceIsReportedOnlyOnce()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 30, characterRefs: ["ghost"]),
                StoryTestSupport.Beat("beat-02", 2, role: "payoff", duration: 30, characterRefs: ["ghost"])
            ],
            targetDuration: 60);

        var issues = StoryContinuityValidator.Validate(
            plan,
            new CharacterBibleRegistry(),
            new WorldBibleRegistry());

        Assert.Single(issues);
        Assert.Equal(BibleIssueCodes.StoryCharacterReferenceUnknown, issues[0].Code);
    }

    [Fact]
    public void PlanWithoutReferencesStillValidates()
    {
        var plan = StoryTestSupport.Plan(targetDuration: 60);

        Assert.Empty(StoryContinuityValidator.Validate(
            plan,
            new CharacterBibleRegistry(),
            new WorldBibleRegistry()));
    }

    [Fact]
    public void AnyRegisteredVersionSatisfiesAReference()
    {
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60, characterRefs: ["rio"])],
            targetDuration: 60);

        var characters = new CharacterBibleRegistry([BibleTestSupport.Character("rio", version: 3)]);
        var worlds = new WorldBibleRegistry();

        Assert.Empty(StoryContinuityValidator.Validate(plan, characters, worlds));
    }
}
