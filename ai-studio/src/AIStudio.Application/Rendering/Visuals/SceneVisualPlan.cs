namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Resolved visual direction for one storyboard scene: the SVG brief plus the
/// selected engine and, for animation, the template and its structured inputs.
/// </summary>
public sealed record SceneVisualPlan(
    SceneVisualBrief Brief,
    SceneVisualEngine Engine,
    SceneAnimationTemplate Template = SceneAnimationTemplate.None,
    SceneAnimationParameters? Animation = null,
    SceneThreeDTemplate ThreeDTemplate = SceneThreeDTemplate.None,
    SceneVisualDirection? Direction = null,
    bool IsFallback = false,
    SceneVisualEngine IntendedEngine = SceneVisualEngine.SvgStill);

/// <summary>
/// The narration window inside one scene. Beats are scheduled within this window so
/// motion explains the words currently being spoken.
/// </summary>
public sealed record SceneNarrationWindow(
    int SceneIndex,
    double NarrationStartWithinScene,
    double NarrationDurationSeconds);
