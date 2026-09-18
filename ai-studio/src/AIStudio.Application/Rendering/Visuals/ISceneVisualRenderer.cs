namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Renders a structured <see cref="SceneVisualBrief"/> to a scene PNG through a
/// local rasterizer. This is the Visual Asset Engine boundary: it produces
/// pixels for the existing FFmpeg renderer and persists nothing itself.
/// </summary>
public interface ISceneVisualRenderer
{
    Task<byte[]> RenderPngAsync(
        SceneVisualBrief brief,
        CancellationToken cancellationToken);
}
