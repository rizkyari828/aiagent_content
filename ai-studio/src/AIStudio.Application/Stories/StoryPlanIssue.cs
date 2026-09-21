namespace AIStudio.Application.Stories;

/// <summary>
/// One deterministic structural problem found in a story plan. A plan with no
/// issues is coherent narrative data; whether it is producible is decided later by
/// the Script / Storyboard / Production layers.
/// </summary>
public sealed record StoryPlanIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>Stable story-plan validation codes, safe to surface to future planners.</summary>
public static class StoryPlanIssueCodes
{
    public const string PlanIdInvalid = "story_plan_id_invalid";
    public const string PlanVersionInvalid = "story_plan_version_invalid";
    public const string SourceConceptInvalid = "story_plan_source_concept_invalid";
    public const string PatternIdInvalid = "story_plan_narrative_pattern_invalid";
    public const string PatternVersionInvalid = "story_plan_narrative_pattern_version_invalid";
    public const string TargetDurationInvalid = "story_plan_target_duration_invalid";
    public const string BeatsEmpty = "story_plan_beats_empty";
    public const string BeatIdInvalid = "story_beat_id_invalid";
    public const string BeatIdDuplicate = "story_beat_id_duplicate";
    public const string BeatOrderInvalid = "story_beat_order_invalid";
    public const string BeatOrderDuplicate = "story_beat_order_duplicate";
    public const string BeatRoleInvalid = "story_beat_role_invalid";
    public const string BeatImportanceInvalid = "story_beat_importance_invalid";
    public const string BeatPurposeEmpty = "story_beat_purpose_empty";
    public const string BeatDurationInvalid = "story_beat_duration_invalid";
    public const string BeatReferenceInvalid = "story_beat_reference_invalid";
    public const string BeatContinuityUnknown = "story_beat_continuity_unknown";
    public const string BeatContinuitySelfReference = "story_beat_continuity_self_reference";
    public const string BeatContinuityCycle = "story_beat_continuity_cycle";
    public const string DurationMismatch = "story_plan_duration_mismatch";
    public const string PatternMismatch = "story_plan_pattern_mismatch";
    public const string RequiredSlotMissing = "story_plan_required_slot_missing";
}
