using System.Text.Json.Serialization;
using AIStudio.Application.Concepts;

namespace AIStudio.Application.Stories;

/// <summary>
/// The Story Director's output: a declarative narrative progression from a
/// <c>CreativeDirection</c>. It references the source concept by identity (it never
/// duplicates <c>ConceptManifest</c>) and is shaped by a data-driven
/// <see cref="NarrativePattern"/>. Beats carry narrative purpose only; words and
/// shot detail belong to Script and Storyboard later.
/// </summary>
public sealed record StoryPlan
{
    [JsonPropertyName("id")]
    public StoryPlanId Id { get; init; }

    [JsonPropertyName("version")]
    public StoryPlanVersion Version { get; init; }

    /// <summary>Identity of the concept this story was planned from; reference only.</summary>
    [JsonPropertyName("sourceConceptId")]
    public ConceptId SourceConceptId { get; init; }

    /// <summary>Data-driven narrative pattern this story follows.</summary>
    [JsonPropertyName("narrativePattern")]
    public NarrativePatternId NarrativePattern { get; init; }

    [JsonPropertyName("narrativePatternVersion")]
    public NarrativePatternVersion NarrativePatternVersion { get; init; }

    [JsonPropertyName("targetDurationSeconds")]
    public int TargetDurationSeconds { get; init; }

    [JsonPropertyName("beats")]
    public IReadOnlyList<StoryBeat> Beats { get; init; } = [];

    /// <summary>Structural validation; producibility is decided by later layers.</summary>
    public IReadOnlyList<StoryPlanIssue> Validate() => StoryPlanValidator.Validate(this);

    /// <summary>Structural validation plus required narrative-pattern slot coverage.</summary>
    public IReadOnlyList<StoryPlanIssue> Validate(NarrativePattern pattern) =>
        StoryPlanValidator.Validate(this, pattern);
}
