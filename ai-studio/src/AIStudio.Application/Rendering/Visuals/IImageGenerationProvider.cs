namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Text-to-image boundary for the optional AI image engine. Implementations turn a
/// single composed prompt into scene PNG bytes through a local image model; they
/// persist nothing themselves. The prompt is data: implementations never accept a
/// model- or planner-supplied graph, only this validated request.
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
