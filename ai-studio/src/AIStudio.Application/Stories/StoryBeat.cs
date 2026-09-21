using System.Text.Json.Serialization;

namespace AIStudio.Application.Stories;

/// <summary>
/// One step in a story's narrative progression. A beat is declarative narrative
/// intent only: it says what should happen and why (role, purpose, importance) and
/// which established characters/worlds it uses, never dialogue, camera, engine, or
/// any executable instruction. Script generation and storyboard/production own the
/// final words and shot detail later.
/// </summary>
public sealed record StoryBeat
{
    [JsonPropertyName("id")]
    public StoryBeatId Id { get; init; }

    /// <summary>1-based position in the narrative. Unique within a plan.</summary>
    [JsonPropertyName("order")]
    public int Order { get; init; }

    /// <summary>Data-driven narrative role, for example <c>hook</c> or <c>chorus</c>.</summary>
    [JsonPropertyName("role")]
    public StoryBeatRole Role { get; init; }

    /// <summary>What this beat communicates; narrative purpose, not final dialogue.</summary>
    [JsonPropertyName("purpose")]
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Data-driven importance token, for example <c>major</c> or <c>minor</c>.</summary>
    [JsonPropertyName("importance")]
    public string Importance { get; init; } = DefaultImportance;

    /// <summary>Target duration for this beat in seconds.</summary>
    [JsonPropertyName("targetDurationSeconds")]
    public int TargetDurationSeconds { get; init; }

    /// <summary>Identifiers of established characters; reference only, no bible here.</summary>
    [JsonPropertyName("characterRefs")]
    public IReadOnlyList<string> CharacterRefs { get; init; } = [];

    /// <summary>Identifiers of established worlds/settings; reference only, no bible here.</summary>
    [JsonPropertyName("worldRefs")]
    public IReadOnlyList<string> WorldRefs { get; init; } = [];

    /// <summary>Earlier beats this beat continues from; reference only.</summary>
    [JsonPropertyName("continuityFrom")]
    public IReadOnlyList<StoryBeatId> ContinuityFrom { get; init; } = [];

    public const string DefaultImportance = "normal";
}
