namespace AIStudio.Application.Stories;

/// <summary>
/// Narrow, deterministic structural validation for a narrative pattern. It checks
/// only the pattern's own data — identity, display name, and each beat slot's role,
/// guidance and weight — so a new narrative format is valid as data. It never
/// scores a story and never consults the registry.
/// </summary>
public static class NarrativePatternValidator
{
    public const int MaximumPurposeLength = 2_000;

    public static IReadOnlyList<NarrativePatternIssue> Validate(NarrativePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var issues = new List<NarrativePatternIssue>();

        if (!StoryIdentifier.IsValid(pattern.Id.Value))
        {
            issues.Add(Issue(
                NarrativePatternIssueCodes.PatternIdInvalid,
                "A narrative pattern id is required and must be a lowercase identifier such as 'problem-solution-short'."));
        }

        if (!pattern.Version.IsValid)
        {
            issues.Add(Issue(
                NarrativePatternIssueCodes.PatternVersionInvalid,
                $"A narrative pattern version must be at least {NarrativePatternVersion.Minimum}."));
        }

        if (string.IsNullOrWhiteSpace(pattern.DisplayName))
        {
            issues.Add(Issue(
                NarrativePatternIssueCodes.DisplayNameEmpty,
                "A narrative pattern display name is required."));
        }

        var slots = pattern.BeatSlots;
        if (slots is null || slots.Count == 0)
        {
            issues.Add(Issue(
                NarrativePatternIssueCodes.SlotsEmpty,
                "A narrative pattern must declare at least one beat slot."));

            return issues;
        }

        foreach (var slot in slots)
        {
            if (slot is null)
            {
                issues.Add(Issue(
                    NarrativePatternIssueCodes.SlotRoleInvalid,
                    "A narrative pattern beat slot is missing."));

                continue;
            }

            if (!StoryIdentifier.IsValid(slot.Role.Value))
            {
                issues.Add(Issue(
                    NarrativePatternIssueCodes.SlotRoleInvalid,
                    "Every narrative pattern beat slot must declare a valid role such as 'hook'."));
            }

            if (string.IsNullOrWhiteSpace(slot.Purpose) || slot.Purpose.Trim().Length > MaximumPurposeLength)
            {
                issues.Add(Issue(
                    NarrativePatternIssueCodes.SlotPurposeEmpty,
                    $"Every narrative pattern beat slot purpose must contain 1 to {MaximumPurposeLength} characters."));
            }

            if (slot.DurationWeight <= 0 || double.IsNaN(slot.DurationWeight) || double.IsInfinity(slot.DurationWeight))
            {
                issues.Add(Issue(
                    NarrativePatternIssueCodes.SlotWeightInvalid,
                    "Every narrative pattern beat slot duration weight must be a positive number."));
            }
        }

        return issues;
    }

    private static NarrativePatternIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
