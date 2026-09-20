namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Deterministic engine router. It takes the director's preferred engine (which the
/// caller must honour when the provider is available) and degrades safely along the
/// documented chain when a provider is disabled:
/// <code>
/// ThreeD  -> Manim -> Animated SVG
/// AiImage -> Manim -> Animated SVG
/// Manim   -> Animated SVG
/// </code>
/// Animated SVG and SVG still are always available because they are CPU-only
/// repository-owned renderers, so a production never fails just because an optional
/// GPU provider is disabled.
/// </summary>
public static class SceneVisualRouter
{
    public static SceneVisualRouteResult Select(
        SceneVisualDirection direction,
        bool manimEnabled,
        bool imageEnabled,
        bool threeDEnabled)
    {
        ArgumentNullException.ThrowIfNull(direction);

        return direction.PreferredEngine switch
        {
            SceneVisualEngine.ThreeD => SelectThreeD(direction, manimEnabled, threeDEnabled),
            SceneVisualEngine.ManimAnimation => SelectManim(direction, manimEnabled),
            SceneVisualEngine.AiImage => SelectAiImage(direction, manimEnabled, imageEnabled),
            _ => Route(
                direction,
                SceneVisualEngine.AnimatedSvg,
                fallback: direction.PreferredEngine != SceneVisualEngine.AnimatedSvg,
                reason: "animated_svg")
        };
    }

    private static SceneVisualRouteResult SelectThreeD(
        SceneVisualDirection direction,
        bool manimEnabled,
        bool threeDEnabled)
    {
        if (threeDEnabled && direction.ThreeDTemplate != SceneThreeDTemplate.None)
        {
            return Route(
                direction,
                SceneVisualEngine.ThreeD,
                fallback: false,
                reason: "threed_available");
        }

        return SelectManim(direction, manimEnabled, "threed_disabled");
    }

    private static SceneVisualRouteResult SelectManim(
        SceneVisualDirection direction,
        bool manimEnabled,
        string reason = "manim_disabled")
    {
        if (manimEnabled && direction.ManimTemplate != SceneAnimationTemplate.None)
        {
            return Route(
                direction,
                SceneVisualEngine.ManimAnimation,
                fallback: direction.PreferredEngine != SceneVisualEngine.ManimAnimation,
                reason: "manim_available");
        }

        return Route(
            direction,
            SceneVisualEngine.AnimatedSvg,
            fallback: direction.PreferredEngine != SceneVisualEngine.AnimatedSvg,
            reason);
    }

    private static SceneVisualRouteResult SelectAiImage(
        SceneVisualDirection direction,
        bool manimEnabled,
        bool imageEnabled)
    {
        if (imageEnabled)
        {
            return Route(direction, SceneVisualEngine.AiImage, fallback: false, reason: "aiimage_available");
        }

        return SelectManim(direction, manimEnabled, "aiimage_disabled");
    }

    private static SceneVisualRouteResult Route(
        SceneVisualDirection direction,
        SceneVisualEngine selected,
        bool fallback,
        string reason) =>
        new(
            direction.PreferredEngine,
            selected,
            fallback,
            reason,
            selected == SceneVisualEngine.ManimAnimation
                ? direction.ManimTemplate
                : SceneAnimationTemplate.None,
            selected == SceneVisualEngine.ThreeD
                ? direction.ThreeDTemplate
                : SceneThreeDTemplate.None);
}
