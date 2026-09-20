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
