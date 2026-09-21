using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class StoryPlanValidatorTests
{
    [Fact]
    public void ValidPlanHasNoIssues()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 20),
                StoryTestSupport.Beat("beat-02", 2, role: "payoff", duration: 40, continuityFrom: ["beat-01"])
            ],
            targetDuration: 60);

        Assert.Empty(plan.Validate());
    }

    [Fact]
    public void InvalidIdentityIsReported()
    {
        var plan = StoryTestSupport.Plan(beats: [StoryTestSupport.Beat("beat-01", 1, duration: 60)]) with
        {
            SourceConceptId = default,
            NarrativePatternVersion = default
        };

        var codes = plan.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(StoryPlanIssueCodes.SourceConceptInvalid, codes);
        Assert.Contains(StoryPlanIssueCodes.PatternVersionInvalid, codes);
    }

    [Fact]
    public void EmptyBeatsAreReported()
    {
        var plan = StoryTestSupport.Plan(beats: []);

        Assert.Contains(StoryPlanIssueCodes.BeatsEmpty, plan.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void DuplicateBeatIdsAreReported()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, duration: 30),
                StoryTestSupport.Beat("beat-01", 2, duration: 30)
            ],
            targetDuration: 60);

        Assert.Contains(StoryPlanIssueCodes.BeatIdDuplicate, plan.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void DuplicateBeatOrdersAreReported()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, duration: 30),
                StoryTestSupport.Beat("beat-02", 1, duration: 30)
            ],
            targetDuration: 60);

        Assert.Contains(StoryPlanIssueCodes.BeatOrderDuplicate, plan.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void NonPositiveOrderAndDurationAndEmptyPurposeAndBadRoleAreReported()
    {
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 0, role: "hook", duration: 0, purpose: string.Empty)],
            targetDuration: 60);

        var codes = plan.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(StoryPlanIssueCodes.BeatOrderInvalid, codes);
        Assert.Contains(StoryPlanIssueCodes.BeatDurationInvalid, codes);
        Assert.Contains(StoryPlanIssueCodes.BeatPurposeEmpty, codes);
    }

    [Fact]
    public void InvalidImportanceIsReported()
    {
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, duration: 60, importance: "Not Valid")],
            targetDuration: 60);

        Assert.Contains(StoryPlanIssueCodes.BeatImportanceInvalid, plan.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void InvalidCharacterAndWorldReferencesAreReported()
    {
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, duration: 60, characterRefs: ["Bad Ref"], worldRefs: ["-bad"])],
            targetDuration: 60);

        Assert.Contains(StoryPlanIssueCodes.BeatReferenceInvalid, plan.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void UnknownContinuityReferenceIsReported()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, duration: 30),
                StoryTestSupport.Beat("beat-02", 2, duration: 30, continuityFrom: ["beat-99"])
            ],
            targetDuration: 60);

        Assert.Contains(StoryPlanIssueCodes.BeatContinuityUnknown, plan.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void SelfContinuityReferenceIsReported()
    {
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, duration: 60, continuityFrom: ["beat-01"])],
            targetDuration: 60);

        Assert.Contains(StoryPlanIssueCodes.BeatContinuitySelfReference, plan.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void ContinuityCycleIsReported()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, duration: 30, continuityFrom: ["beat-02"]),
                StoryTestSupport.Beat("beat-02", 2, duration: 30, continuityFrom: ["beat-01"])
            ],
            targetDuration: 60);

        Assert.Contains(StoryPlanIssueCodes.BeatContinuityCycle, plan.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void DurationMismatchIsReported()
    {
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, duration: 10)],
            targetDuration: 60);

        Assert.Contains(StoryPlanIssueCodes.DurationMismatch, plan.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void RoundingWithinToleranceIsAccepted()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, duration: 20),
                StoryTestSupport.Beat("beat-02", 2, duration: 20),
                StoryTestSupport.Beat("beat-03", 3, duration: 19)
            ],
            targetDuration: 60);

        Assert.Empty(plan.Validate());
    }

    [Fact]
    public void RequiredSlotMissingIsReported()
    {
        var pattern = StoryTestSupport.Pattern(
            "problem-solution-short",
            slots: [StoryTestSupport.Slot("hook"), StoryTestSupport.Slot("payoff")]);

        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60)],
            targetDuration: 60);

        Assert.Contains(
            StoryPlanIssueCodes.RequiredSlotMissing,
            plan.Validate(pattern).Select(issue => issue.Code));
    }

    [Fact]
    public void PatternMismatchIsReported()
    {
        var pattern = StoryTestSupport.Pattern("other-pattern", slots: [StoryTestSupport.Slot("hook")]);
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60)],
            targetDuration: 60);

        Assert.Contains(
            StoryPlanIssueCodes.PatternMismatch,
            plan.Validate(pattern).Select(issue => issue.Code));
    }

    [Fact]
    public void OptionalSlotsDoNotBlockValidation()
    {
        var pattern = StoryTestSupport.Pattern(
            "problem-solution-short",
            slots: [StoryTestSupport.Slot("hook"), StoryTestSupport.Slot("payoff", required: false)]);

        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60)],
            targetDuration: 60);

        Assert.Empty(plan.Validate(pattern));
    }
}
