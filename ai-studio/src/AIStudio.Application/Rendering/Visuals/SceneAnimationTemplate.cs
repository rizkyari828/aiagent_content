namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Predefined, repository-owned animation templates. Model or planner output may
/// only choose one of these names; it never supplies executable Python. An unknown
/// name is rejected by the Manim renderer boundary.
/// </summary>
public enum SceneAnimationTemplate
{
    None = 0,

    /// <summary>Laptop -> data flows to a cloud that is crossed out -> data stays local.</summary>
    LocalAiFlow = 1,

    /// <summary>User message appears -> processing indicator -> assistant reply appears.</summary>
    ChatFlow = 2
}
