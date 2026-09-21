namespace AIStudio.Application.Stories;

/// <summary>
/// One deterministic structural problem found in a narrative pattern. New
/// narrative patterns register as data and are validated structurally only; a
/// pattern never describes an executable capability.
/// </summary>
public sealed record NarrativePatternIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>Stable narrative-pattern validation codes, safe to surface to data loaders.</summary>
public static class NarrativePatternIssueCodes
{
    public const string PatternIdInvalid = "narrative_pattern_id_invalid";
    public const string PatternVersionInvalid = "narrative_pattern_version_invalid";
    public const string DisplayNameEmpty = "narrative_pattern_display_name_empty";
    public const string SlotsEmpty = "narrative_pattern_slots_empty";
    public const string SlotRoleInvalid = "narrative_pattern_slot_role_invalid";
    public const string SlotPurposeEmpty = "narrative_pattern_slot_purpose_empty";
    public const string SlotWeightInvalid = "narrative_pattern_slot_weight_invalid";
}
