namespace AIStudio.Application.Rendering.AudioProduction;

/// <summary>
/// Assembles validated per-scene narration clips into one narration track at
/// deterministic offsets. CPU-only: implementations must not acquire the GPU gate.
/// </summary>
public interface IAudioNarrationAssembler
{
    Task<byte[]> AssembleAsync(
        IReadOnlyList<NarrationSegment> segments,
        double totalDurationSeconds,
        CancellationToken cancellationToken);
}
