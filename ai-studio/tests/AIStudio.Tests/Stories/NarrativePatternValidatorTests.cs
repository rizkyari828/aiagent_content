using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class NarrativePatternValidatorTests
{
    [Fact]
    public void ValidPatternHasNoIssues()
    {
        Assert.Empty(StoryTestSupport.Pattern().Validate());
    }

    [Fact]
    public void InvalidIdentityAndDisplayNameAreReported()
    {
        var pattern = new NarrativePattern
        {
            DisplayName = string.Empty,
            BeatSlots = [StoryTestSupport.Slot("hook")]
        };

        var codes = pattern.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(NarrativePatternIssueCodes.PatternIdInvalid, codes);
        Assert.Contains(NarrativePatternIssueCodes.PatternVersionInvalid, codes);
        Assert.Contains(NarrativePatternIssueCodes.DisplayNameEmpty, codes);
    }

    [Fact]
    public void EmptySlotsAreReported()
    {
        var pattern = new NarrativePattern
        {
            Id = new NarrativePatternId("empty-pattern"),
            Version = new NarrativePatternVersion(1),
            DisplayName = "Empty",
            BeatSlots = []
        };

        var codes = pattern.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(NarrativePatternIssueCodes.SlotsEmpty, codes);
    }

    [Fact]
    public void InvalidSlotRolePurposeAndWeightAreReported()
    {
        var pattern = new NarrativePattern
        {
            Id = new NarrativePatternId("bad-slots"),
            Version = new NarrativePatternVersion(1),
            DisplayName = "Bad slots",
            BeatSlots =
            [
                new NarrativePatternBeatSlot
                {
                    Role = default,
                    Purpose = string.Empty,
                    DurationWeight = 0
                }
            ]
        };

        var codes = pattern.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(NarrativePatternIssueCodes.SlotRoleInvalid, codes);
        Assert.Contains(NarrativePatternIssueCodes.SlotPurposeEmpty, codes);
        Assert.Contains(NarrativePatternIssueCodes.SlotWeightInvalid, codes);
    }
}
