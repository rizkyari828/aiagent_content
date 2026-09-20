namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Maps the planner's chosen templates to an output engine. Priority preserves
/// existing behavior: a Manim animation template always wins; otherwise a
/// supported Blender template selects the 3D engine; otherwise the optional AI
/// image engine, and finally the deterministic SVG still.
/// </summary>
public static class SceneVisualEngineSelector
{
    public static SceneVisualEngine Select(
        SceneAnimationTemplate template,
        bool enableAiImages = false,
        SceneThreeDTemplate threeDTemplate = SceneThreeDTemplate.None)
    {
        if (template != SceneAnimationTemplate.None)
        {
            return SceneVisualEngine.ManimAnimation;
        }

        if (threeDTemplate != SceneThreeDTemplate.None)
        {
            return SceneVisualEngine.ThreeD;
        }

        return enableAiImages
            ? SceneVisualEngine.AiImage
            : SceneVisualEngine.SvgStill;
    }
}
