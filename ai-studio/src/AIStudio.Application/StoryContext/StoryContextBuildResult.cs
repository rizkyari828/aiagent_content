namespace AIStudio.Application.StoryContext;

/// <summary>
/// The result of one context build: the projected <see cref="StoryContext"/> plus any
/// deterministic issues. The context is still returned when issues exist (for example
/// an unresolved reference is reported and omitted rather than silently dropped).
/// </summary>
public sealed record StoryContextBuildResult
{
    public StoryContext Context { get; init; } = new();

    public IReadOnlyList<StoryContextIssue> Issues { get; init; } = [];

    public bool IsValid => Issues.Count == 0;
}
