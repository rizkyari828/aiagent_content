namespace AIStudio.Application.Rendering;

/// <summary>
/// Transition applied between consecutive scenes. <see cref="Crossfade"/> overlaps
/// scenes and therefore shifts per-scene timing; <see cref="Cut"/> and
/// <see cref="Fade"/> keep every scene at its full duration.
/// </summary>
public enum SceneTransition
{
    Cut,
    Fade,
    Crossfade
}
