namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Curated, trusted animation primitives. Storyboard/planner data may select one of
/// these names only; it can never supply executable code. The renderer maps each
/// primitive to a deterministic effect inside the repository-owned composer.
/// </summary>
public enum AnimationPrimitive
{
    FadeIn = 0,
    FadeOut = 1,
    SlideIn = 2,
    SlideOut = 3,
    ScaleIn = 4,
    ScalePulse = 5,
    Move = 6,
    Reveal = 7,
    DrawLine = 8,
    Progress = 9,
    TypeText = 10,
    Checkmark = 11,
    Highlight = 12,
    Counter = 13
}
