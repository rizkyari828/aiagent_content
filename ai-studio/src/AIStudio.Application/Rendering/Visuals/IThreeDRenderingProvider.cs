namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Renders one predefined, repository-owned Blender template to a short MP4 clip.
/// The template identifier and structured parameters are data; the Blender Python
/// is repository-owned and trusted. Implementations must never accept or execute
/// caller-supplied source, paths, or shell fragments.
/// </summary>
public interface IThreeDRenderingProvider
{
    /// <summary>
    /// False when Blender is not configured. The planner then stays on the
    /// deterministic still engines, so a missing runtime degrades safely instead of
    /// failing jobs.
    /// </summary>
    bool IsEnabled { get; }

    Task<byte[]> RenderAsync(
        ThreeDRenderRequest request,
        CancellationToken cancellationToken);
}
