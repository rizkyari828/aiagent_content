namespace AIStudio.Application.Creative;

/// <summary>
/// Narrow structural validation for a creative treatment: the required descriptive
/// fields must be present and bounded. It does not score creativity and does not
/// require any particular treatment vocabulary, so data-driven names such as
/// <c>retro-game-documentary</c> remain valid.
/// </summary>
public static class CreativeTreatmentValidator
{
    public const int MaximumTextLength = 2_000;

    public static IReadOnlyList<CreativeIssue> Validate(CreativeTreatment treatment)
    {
        ArgumentNullException.ThrowIfNull(treatment);

        var issues = new List<CreativeIssue>();

        Require(treatment.StoryApproach, CreativeIssueCodes.TreatmentStoryApproachEmpty, "storyApproach", issues);
        Require(treatment.HookTreatment, CreativeIssueCodes.TreatmentHookEmpty, "hookTreatment", issues);
        Require(treatment.Pacing, CreativeIssueCodes.TreatmentPacingEmpty, "pacing", issues);
        Require(treatment.VisualStrategy, CreativeIssueCodes.TreatmentVisualStrategyEmpty, "visualStrategy", issues);
        Require(treatment.EndingTreatment, CreativeIssueCodes.TreatmentEndingEmpty, "endingTreatment", issues);

        return issues;
    }

    private static void Require(
        string? value,
        string code,
        string fieldName,
        List<CreativeIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > MaximumTextLength)
        {
            issues.Add(new CreativeIssue
            {
                Code = code,
                Message = $"Creative treatment field '{fieldName}' must contain 1 to {MaximumTextLength} characters."
            });
        }
    }
}
