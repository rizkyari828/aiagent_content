namespace AIStudio.Application.Rendering;

/// <summary>
/// Coarse visual classification used by the transition policy. Text-heavy /
/// interface / infographic scenes must not crossfade onto one another because the
/// overlap briefly blends two text layers into unreadable ghosting. Photographic
/// and illustrative scenes tolerate a crossfade. Classification is deterministic;
/// no computer vision is involved.
/// </summary>
public enum SceneVisualKind
{
    /// <summary>Photographic or illustrative scene; crossfade is allowed.</summary>
    Photographic = 0,

    /// <summary>
    /// Text-heavy, UI, or infographic scene. Crossfade is replaced with a
    /// fade-through-background so two text layers never overlay.</summary>
    Graphic = 1
}
