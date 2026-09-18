namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Maps a chosen animation template to an output engine. A template means the
/// scene is animated; no template means the deterministic SVG still.
/// </summary>
public static class SceneVisualEngineSelector
{
    public static SceneVisualEngine Select(SceneAnimationTemplate template) =>
        template == SceneAnimationTemplate.None
            ? SceneVisualEngine.SvgStill
            : SceneVisualEngine.ManimAnimation;
}
