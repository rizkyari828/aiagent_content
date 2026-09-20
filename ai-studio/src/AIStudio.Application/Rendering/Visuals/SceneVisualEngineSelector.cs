namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Maps a chosen animation template to an output engine. A template means the
/// scene is animated; otherwise the deterministic SVG still is used, unless the
/// optional local AI image engine is enabled, in which case it renders the still.
/// </summary>
public static class SceneVisualEngineSelector
{
    public static SceneVisualEngine Select(
        SceneAnimationTemplate template,
        bool enableAiImages = false)
    {
        if (template != SceneAnimationTemplate.None)
        {
            return SceneVisualEngine.ManimAnimation;
        }

        return enableAiImages
            ? SceneVisualEngine.AiImage
            : SceneVisualEngine.SvgStill;
    }
}
