using System.Text.Json.Serialization;

namespace AIStudio.Application.Creative;

/// <summary>
/// HOW the approved idea should feel and flow creatively: story approach, hook,
/// pacing, visual strategy, and ending. It is descriptive data only — no script,
/// scene-by-scene storyboard, provider prompt, or executable instruction. Field
/// values are strings, so a new treatment name never needs an enum or subtype.
/// </summary>
public sealed record CreativeTreatment
{
    [JsonPropertyName("storyApproach")]
    public string StoryApproach { get; init; } = string.Empty;

    [JsonPropertyName("hookTreatment")]
    public string HookTreatment { get; init; } = string.Empty;

    [JsonPropertyName("pacing")]
    public string Pacing { get; init; } = string.Empty;

    [JsonPropertyName("visualStrategy")]
    public string VisualStrategy { get; init; } = string.Empty;

    [JsonPropertyName("endingTreatment")]
    public string EndingTreatment { get; init; } = string.Empty;

    [JsonPropertyName("tone")]
    public string? Tone { get; init; }

    [JsonPropertyName("transitionStrategy")]
    public string? TransitionStrategy { get; init; }

    public IReadOnlyList<CreativeIssue> Validate() => CreativeTreatmentValidator.Validate(this);
}
