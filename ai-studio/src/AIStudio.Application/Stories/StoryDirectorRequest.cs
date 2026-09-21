using AIStudio.Application.Creative;

namespace AIStudio.Application.Stories;

/// <summary>
/// Input for one story-directing call. It consumes the existing
/// <see cref="CreativeDirection"/> (the approved idea's concept + treatment) rather
/// than re-inventing the idea or duplicating <c>ConceptManifest</c>. For v1 the
/// caller supplies the narrative pattern explicitly; no heuristic or LLM pattern
/// selection exists yet.
/// </summary>
public sealed record StoryDirectorRequest
{
    /// <summary>The creative direction to turn into a narrative progression.</summary>
    public CreativeDirection CreativeDirection { get; init; } = new();

    /// <summary>The data-driven narrative pattern this story should follow.</summary>
    public NarrativePatternId NarrativePattern { get; init; }

    /// <summary>Requested pattern version; null means the latest registered version.</summary>
    public NarrativePatternVersion? NarrativePatternVersion { get; init; }

    /// <summary>Target duration in seconds; null uses the source concept duration.</summary>
    public int? TargetDurationSeconds { get; init; }

    /// <summary>Explicit plan id; null derives one from the source concept identity.</summary>
    public StoryPlanId? PlanId { get; init; }

    /// <summary>Plan version; null defaults to 1.</summary>
    public StoryPlanVersion? Version { get; init; }
}

/// <summary>
/// The Story Director's result for one call: the planned narrative progression.
/// The plan is data only and is not registered, approved, or produced here.
/// </summary>
public sealed record StoryDirectorResult
{
    public StoryPlan Plan { get; init; } = new();
}
