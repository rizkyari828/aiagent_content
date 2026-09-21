namespace AIStudio.Application.Stories;

/// <summary>
/// Raised when a story cannot be directed from the supplied request. The
/// <see cref="Code"/> is stable and safe to surface to future callers. Story data
/// is never executed.
/// </summary>
public sealed class StoryDirectorException : Exception
{
    public StoryDirectorException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable Story Director error codes.</summary>
public static class StoryDirectorErrorCodes
{
    public const string RequestInvalid = "story_request_invalid";
    public const string PatternNotFound = "story_pattern_not_found";
    public const string PlanInvalid = "story_plan_invalid";
}
