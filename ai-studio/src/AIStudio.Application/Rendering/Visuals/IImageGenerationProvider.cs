namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Image-generation boundary for the optional local engine. Implementations accept
/// either text only or one already-pinned approved identity reference; they persist
/// nothing themselves. The prompt is data: implementations never accept a caller-
/// supplied graph, model, checkpoint, path, or provider configuration.
/// </summary>
public interface IImageGenerationProvider
{
    /// <summary>
    /// False when no local image model is configured. The planner then stays on the
    /// deterministic engines, so a missing runtime degrades safely instead of
    /// failing jobs.
    /// </summary>
    bool IsEnabled { get; }

    Task<byte[]> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken);
}
