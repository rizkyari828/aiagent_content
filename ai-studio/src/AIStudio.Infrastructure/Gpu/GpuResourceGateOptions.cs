namespace AIStudio.Infrastructure.Gpu;

/// <summary>
/// Configuration for the process-local GPU resource gate. Enabled by default: it
/// only serializes heavy GPU workloads and does not change functional results, so
/// it safely protects the current single-GPU machine without a feature flag.
/// </summary>
public sealed class GpuResourceGateOptions
{
    public const string SectionName = "GpuResourceGate";

    public bool Enabled { get; set; } = true;
}
