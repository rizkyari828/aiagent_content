namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Image-to-video boundary for the optional local engine. Implementations accept a
/// prompt, generation parameters, and at most one already-pinned approved identity
/// frame. They persist the artifact themselves through the application asset store
/// and return neutral metadata only. The request is data: implementations never
/// accept a caller-supplied workflow, node graph, model, checkpoint, path, URL, or
/// provider configuration.
/// </summary>
public interface IVideoGenerationProvider
{
    /// <summary>
    /// False when no local video model is configured. Production routing then keeps
    /// scenes on the existing still/animation engines, so a missing runtime degrades
    /// safely instead of failing jobs. v1 is unselected by every production path.
    /// </summary>
    bool IsEnabled { get; }

    Task<VideoGenerationResult> GenerateAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken);
}
