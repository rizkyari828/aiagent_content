namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Renders one predefined animation template to a short MP4 clip. The template
/// and its parameters are data; the template source is repository-owned Python.
/// Implementations must never execute model-generated code.
/// </summary>
public interface IManimSceneRenderer
{
    /// <summary>
    /// False when Manim is not configured. The planner then stays on the SVG still
    /// engine, so a missing runtime degrades safely instead of failing jobs.
    /// </summary>
    bool IsEnabled { get; }

    Task<byte[]> RenderAsync(
        SceneAnimationTemplate template,
        SceneAnimationParameters parameters,
        CancellationToken cancellationToken);
}
