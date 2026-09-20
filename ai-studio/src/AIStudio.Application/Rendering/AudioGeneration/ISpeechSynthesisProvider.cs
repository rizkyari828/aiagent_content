namespace AIStudio.Application.Rendering.AudioGeneration;

/// <summary>
/// Local text-to-speech boundary. Implementations synthesize one WAV from a
/// validated request and persist it through the approved asset store. They never
/// accept caller-supplied code, executable paths, model paths, or shell text.
/// </summary>
public interface ISpeechSynthesisProvider
{
    /// <summary>
    /// False when no speech runtime is configured. Callers then treat speech as
    /// unavailable instead of failing.
    /// </summary>
    bool IsEnabled { get; }

    Task<SpeechSynthesisResult> SynthesizeAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken);
}
