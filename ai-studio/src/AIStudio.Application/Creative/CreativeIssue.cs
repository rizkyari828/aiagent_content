namespace AIStudio.Application.Creative;

/// <summary>
/// One deterministic problem found in a creative input, treatment, or model
/// response. A creative direction is untrusted data until it passes structural
/// validation; nothing here executes code or scores quality.
/// </summary>
public sealed record CreativeIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>Stable creative-layer codes, safe to surface to future planners.</summary>
public static class CreativeIssueCodes
{
    public const string IdeaInvalid = "creative_direction_invalid_idea";
    public const string IdeaTopicEmpty = "creative_idea_topic_empty";
    public const string IdeaAngleEmpty = "creative_idea_angle_empty";
    public const string IdeaAudienceEmpty = "creative_idea_audience_empty";
    public const string IdeaHookEmpty = "creative_idea_hook_empty";
    public const string IdeaPreferredFormatInvalid = "creative_idea_preferred_format_invalid";
    public const string IdeaPreferredStyleInvalid = "creative_idea_preferred_style_invalid";
    public const string IdeaDurationInvalid = "creative_idea_duration_invalid";

    public const string TreatmentInvalid = "creative_direction_invalid_treatment";
    public const string TreatmentStoryApproachEmpty = "creative_treatment_story_approach_empty";
    public const string TreatmentHookEmpty = "creative_treatment_hook_empty";
    public const string TreatmentPacingEmpty = "creative_treatment_pacing_empty";
    public const string TreatmentVisualStrategyEmpty = "creative_treatment_visual_strategy_empty";
    public const string TreatmentEndingEmpty = "creative_treatment_ending_empty";

    public const string DirectionInvalidJson = "creative_direction_invalid_json";
    public const string DirectionMissingConcept = "creative_direction_missing_concept";
    public const string DirectionMissingTreatment = "creative_direction_missing_treatment";
    public const string DirectionInvalidConcept = "creative_direction_invalid_concept";
    public const string DirectionInvalidTreatment = "creative_direction_invalid_treatment";
}
