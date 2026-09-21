namespace AIStudio.Application.Stories;

/// <summary>
/// Turns a creative direction into a coherent narrative progression as a
/// <see cref="StoryPlan"/>. It consumes an already-directed concept and treatment,
/// shapes it with a data-driven <see cref="NarrativePattern"/>, and never generates
/// the idea, chooses the creative treatment, writes final dialogue, or produces
/// storyboard/shot detail. Those stay with the Solution Idea, Creative Director,
/// Script, and Storyboard layers respectively.
/// </summary>
public interface IStoryDirector
{
    StoryDirectorResult Direct(StoryDirectorRequest request);
}
