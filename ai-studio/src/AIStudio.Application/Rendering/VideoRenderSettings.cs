namespace AIStudio.Application.Rendering;

/// <summary>
/// Resolved render settings passed to the command plan. Every optional value has
/// a deterministic default so an existing render request keeps working unchanged.
/// </summary>
public sealed record VideoRenderSettings(
    int Width,
    int Height,
    int FrameRate,
    double NarrationDurationSeconds,
    SceneTransition Transition = SceneTransition.Crossfade,
    double TransitionDurationSeconds = 0.35,
    bool EnableMotion = true,
    string? SubtitlePath = null,
    SubtitleStyle? Subtitle = null,
    BackgroundMusic? BackgroundMusic = null);
