namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Output engine selected for one scene visual. Kept as a small closed enum:
/// a plugin registry is deliberately not introduced.
/// </summary>
public enum SceneVisualEngine
{
    /// <summary>Deterministic SVG composed to a still PNG.</summary>
    SvgStill = 0,

    /// <summary>Repository-owned Manim template rendered to a short clip.</summary>
    ManimAnimation = 1,

    /// <summary>Local text-to-image model rendered to a still PNG.</summary>
    AiImage = 2,

    /// <summary>Repository-owned Blender template rendered to a short clip.</summary>
    ThreeD = 3,

    /// <summary>Repository-owned animated SVG motion-graphics clip (CPU/FFmpeg).</summary>
    AnimatedSvg = 4
}
