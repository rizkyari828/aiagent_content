namespace AIStudio.Application.Rendering;

/// <summary>
/// Process-local exclusive lease for heavy GPU workloads. AI Studio runs on a
/// single consumer GPU, so at most one heavy workload (local image/3D/text model)
/// may execute at a time inside this process. The gate is deliberately minimal: no
/// priorities, fairness, VRAM accounting, or cross-process coordination.
/// </summary>
public interface IGpuResourceGate
{
    /// <summary>
    /// Waits asynchronously until the exclusive GPU lease is available, then
    /// returns it. Dispose (async) the lease to release it exactly once. Waiting
    /// honors <paramref name="cancellationToken"/> without consuming the lease.
    /// </summary>
    ValueTask<IAsyncDisposable> AcquireAsync(
        string workloadName,
        CancellationToken cancellationToken);
}

/// <summary>Stable workload names for GPU-gate observability.</summary>
public static class GpuWorkloads
{
    public const string ComfyUi = "comfyui";

    public const string Blender = "blender";

    public const string Ollama = "ollama";

    public const string SpeechSynthesis = "speech";

    public const string MusicGeneration = "music";
}
