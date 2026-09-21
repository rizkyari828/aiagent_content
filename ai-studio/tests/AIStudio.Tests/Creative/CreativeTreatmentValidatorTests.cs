using AIStudio.Application.Creative;
using Xunit;

namespace AIStudio.Tests.Creative;

public sealed class CreativeTreatmentValidatorTests
{
    [Fact]
    public void ValidTreatmentHasNoIssues()
    {
        var treatment = new CreativeTreatment
        {
            StoryApproach = "problem-discovery-payoff",
            HookTreatment = "cinematic developer struggling with API cost",
            Pacing = "fast",
            VisualStrategy = "character-led opening, technical clarity in the middle",
            EndingTreatment = "show local AI running successfully"
        };

        Assert.Empty(CreativeTreatmentValidator.Validate(treatment));
    }

    [Fact]
    public void MissingRequiredTreatmentFieldsAreRejected()
    {
        var codes = Codes(CreativeTreatmentValidator.Validate(new CreativeTreatment()));

        Assert.Contains(CreativeIssueCodes.TreatmentStoryApproachEmpty, codes);
        Assert.Contains(CreativeIssueCodes.TreatmentHookEmpty, codes);
        Assert.Contains(CreativeIssueCodes.TreatmentPacingEmpty, codes);
        Assert.Contains(CreativeIssueCodes.TreatmentVisualStrategyEmpty, codes);
        Assert.Contains(CreativeIssueCodes.TreatmentEndingEmpty, codes);
    }

    [Fact]
    public void TreatmentNamesStayDataDriven()
    {
        var treatment = new CreativeTreatment
        {
            StoryApproach = "retro-game-documentary",
            HookTreatment = "arcade screen flickering to life",
            Pacing = "slow-burn",
            VisualStrategy = "pixel-art overlays",
            EndingTreatment = "ranked list payoff",
            Tone = "nostalgic",
            TransitionStrategy = "crt-glitch"
        };

        Assert.Empty(CreativeTreatmentValidator.Validate(treatment));
    }

    private static IReadOnlyList<string> Codes(IReadOnlyList<CreativeIssue> issues) =>
        issues.Select(issue => issue.Code).ToList();
}
