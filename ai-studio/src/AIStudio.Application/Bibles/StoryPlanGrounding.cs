using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Explicit grounding assignment for one <see cref="StoryBeat"/>: the character and
/// world bible ids that beat should reference. It carries stable ids only — never a
/// provider, path, or executable — and it never redefines narrative content.
/// </summary>
public sealed record StoryBeatGrounding
{
    public StoryBeatId BeatId { get; init; }

    /// <summary>Replacement character refs for this beat; an empty list clears them.</summary>
    public IReadOnlyList<string> CharacterRefs { get; init; } = [];

    /// <summary>Replacement world refs for this beat; an empty list clears them.</summary>
    public IReadOnlyList<string> WorldRefs { get; init; } = [];
}

/// <summary>
/// Input for one deterministic grounding pass: the existing story plan plus an
/// explicit per-beat assignment map. Beats not listed keep their existing refs, so
/// grounding is always explicit and never implicitly applied to every beat.
/// </summary>
public sealed record StoryPlanGroundingRequest
{
    public StoryPlan StoryPlan { get; init; } = new();

    public IReadOnlyList<StoryBeatGrounding> Beats { get; init; } = [];
}

/// <summary>
/// Raised when a story plan cannot be grounded from the supplied assignments. The
/// <see cref="Code"/> is stable and safe to surface. Story data is never executed.
/// </summary>
public sealed class StoryPlanGroundingException : Exception
{
    public StoryPlanGroundingException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable Story Plan grounding error codes.</summary>
public static class StoryPlanGroundingErrorCodes
{
    public const string RequestInvalid = "story_grounding_request_invalid";
    public const string BeatUnknown = "story_grounding_beat_unknown";
    public const string BeatDuplicate = "story_grounding_beat_duplicate";
    public const string CharacterUnknown = "story_grounding_character_unknown";
    public const string WorldUnknown = "story_grounding_world_unknown";
    public const string PlanInvalid = "story_grounding_plan_invalid";
}
