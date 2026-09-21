namespace AIStudio.Application.StoryContext;

/// <summary>
/// Projects the trusted narrative inputs (creative direction, story plan, referenced
/// character/world bibles) into a compact, deterministic, consumer-neutral
/// <see cref="StoryContext"/>. It selects, filters, deduplicates, orders, and
/// validates — it never invents content, summarizes with AI, ranks, generates beats,
/// chooses engines, or executes a provider. It is safe for token-efficient future
/// Script and Storyboard prompting.
/// </summary>
public interface IStoryContextBuilder
{
    StoryContextBuildResult Build(StoryContextRequest request);
}
