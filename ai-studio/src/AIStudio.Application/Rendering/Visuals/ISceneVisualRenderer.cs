namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Renders structured scene visuals through a local rasterizer/compositor. This is
/// the Visual Asset Engine boundary: it produces pixels (or a short motion clip)
/// for the existing FFmpeg renderer and persists nothing itself.
/// </summary>
public interface ISceneVisualRenderer
{
    Task<byte[]> RenderPngAsync(
        SceneVisualBrief brief,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders a deterministic, repository-owned animated SVG clip from the brief
    /// and its choreography beats. No caller code is executed.
    /// </summary>
    Task<byte[]> RenderAnimationAsync(
        SceneVisualBrief brief,
        SceneChoreography choreography,
        double durationSeconds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies a lightweight motion treatment to an AI-generated still: a slow
    /// controlled push plus an animated overlay badge. The still stays the hero.
    /// </summary>
    Task<byte[]> RenderImageMotionAsync(
        byte[] backgroundPng,
        string overlayLabel,
        SceneVisualPalette palette,
        double durationSeconds,
        CancellationToken cancellationToken);
}
