namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Structured, pre-validated inputs for an animation template. These are data
/// only: the repository owns the template code, the planner only supplies text,
/// palette, and duration. Nothing here is executed as source.
/// </summary>
public sealed record SceneAnimationParameters(
    string Kicker,
    string PrimaryText,
    string? SecondaryText,
    string? TertiaryText,
    SceneVisualPalette Palette,
    double DurationSeconds);
