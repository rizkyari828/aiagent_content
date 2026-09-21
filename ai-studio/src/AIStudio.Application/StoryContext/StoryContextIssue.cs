namespace AIStudio.Application.StoryContext;

/// <summary>
/// One deterministic problem encountered while projecting story context. Unresolved
/// character/world references are never silently dropped: they are reported here
/// using the shared bible-continuity codes.
/// </summary>
public sealed record StoryContextIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>Stable story-context issue codes.</summary>
public static class StoryContextIssueCodes
{
    /// <summary>The request had no usable story plan or beats.</summary>
    public const string RequestInvalid = "story_context_request_invalid";

    /// <summary>The requested beat id is not present in the story plan.</summary>
    public const string BeatNotFound = "story_context_beat_not_found";
}
