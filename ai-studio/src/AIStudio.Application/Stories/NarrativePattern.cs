using System.Text.Json.Serialization;

namespace AIStudio.Application.Stories;

/// <summary>
/// A reusable narrative pattern: the high-level structural shape a story follows,
/// such as <c>problem-solution-short</c>. A pattern is pure data — an id, a
/// version, a display name, and an ordered list of beat slots. It never carries
/// C# behavior, so a brand-new format (anime horror, kids song, ...) registers as
/// data with no new type, enum, director subclass, or registry change.
/// </summary>
public sealed record NarrativePattern
{
    [JsonPropertyName("id")]
    public NarrativePatternId Id { get; init; }

    [JsonPropertyName("version")]
    public NarrativePatternVersion Version { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("beatSlots")]
    public IReadOnlyList<NarrativePatternBeatSlot> BeatSlots { get; init; } = [];

    /// <summary>Structural validation; pattern availability is decided by the registry.</summary>
    public IReadOnlyList<NarrativePatternIssue> Validate() => NarrativePatternValidator.Validate(this);
}

/// <summary>
/// One structural slot inside a <see cref="NarrativePattern"/>. It says which
/// narrative role belongs at this position, its guidance, and its relative share
/// of the target duration. It is data only: no provider, path, or executable.
/// </summary>
public sealed record NarrativePatternBeatSlot
{
    /// <summary>Data-driven role this slot expects, for example <c>problem</c>.</summary>
    [JsonPropertyName("role")]
    public StoryBeatRole Role { get; init; }

    /// <summary>Guidance for what this slot should communicate.</summary>
    [JsonPropertyName("purpose")]
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Relative share of the story duration; must be positive. Default 1.</summary>
    [JsonPropertyName("durationWeight")]
    public double DurationWeight { get; init; } = 1.0;

    /// <summary>
    /// Whether a valid plan must contain a beat for this slot's role. Default true.
    /// </summary>
    [JsonPropertyName("required")]
    public bool IsRequired { get; init; } = true;
}
