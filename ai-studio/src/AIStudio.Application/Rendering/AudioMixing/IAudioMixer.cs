namespace AIStudio.Application.Rendering.AudioMixing;

/// <summary>
/// Deterministic audio mastering boundary: narration plus optional background
/// music into one normalized, ducked, faded and limited 48 kHz stereo mix.
/// Implementation is CPU-only and does not acquire the GPU resource gate.
/// </summary>
public interface IAudioMixer
{
    Task<AudioMixOutput> MixAsync(
        AudioMixRequest request,
        CancellationToken cancellationToken);
}
