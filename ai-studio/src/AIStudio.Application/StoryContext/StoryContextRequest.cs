using AIStudio.Application.Bibles;
using AIStudio.Application.Creative;
using AIStudio.Application.Stories;

namespace AIStudio.Application.StoryContext;

/// <summary>
/// Input for one context build. It consumes the existing trusted models —
/// <see cref="CreativeDirection"/>, <see cref="StoryPlan"/>, and the bible registries
/// — and projects them; it never duplicates or mutates them. A null
/// <see cref="BeatId"/> builds whole-story context; a set one builds beat-scoped
/// context. Mutable state is optional: identity alone is enough.
/// </summary>
public sealed record StoryContextRequest
{
    public CreativeDirection CreativeDirection { get; init; } = new();

    public StoryPlan StoryPlan { get; init; } = new();

    /// <summary>When set, only this beat and its continuity dependencies are projected.</summary>
    public StoryBeatId? BeatId { get; init; }

    /// <summary>
    /// When true, safe id-only asset metadata is included. Default false: generic
    /// story context excludes asset references unless a consumer needs them.
    /// </summary>
    public bool IncludeAssetReferences { get; init; }

    /// <summary>Optional caller-supplied character state, projected separately from identity.</summary>
    public IReadOnlyList<CharacterState> CharacterStates { get; init; } = [];

    /// <summary>Optional caller-supplied world state, projected separately from identity.</summary>
    public IReadOnlyList<WorldState> WorldStates { get; init; } = [];
}
