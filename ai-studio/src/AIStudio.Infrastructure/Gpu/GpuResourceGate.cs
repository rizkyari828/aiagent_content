using System.Diagnostics;
using AIStudio.Application.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Gpu;

/// <summary>
/// Minimal in-process exclusive GPU gate backed by a single-permit
/// <see cref="SemaphoreSlim"/>. Callers receive an async-disposable lease and can
/// never release manually, so a forgotten or double release is impossible. This is
/// process-local only: it does not coordinate other AI Studio processes, manually
/// launched ComfyUI/Blender jobs, external Ollama clients, or Windows GPU work.
/// </summary>
public sealed class GpuResourceGate : IGpuResourceGate
{
    private readonly SemaphoreSlim semaphore = new(initialCount: 1, maxCount: 1);
    private readonly GpuResourceGateOptions options;
    private readonly ILogger<GpuResourceGate> logger;

    public GpuResourceGate(
        IOptions<GpuResourceGateOptions> options,
        ILogger<GpuResourceGate> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string workloadName,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return NoopLease.Instance;
        }

        if (semaphore.CurrentCount == 0)
        {
            logger.LogInformation(
                "Waiting for the exclusive GPU lease (workload {Workload}).",
                workloadName);
        }

        var start = Stopwatch.GetTimestamp();
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        var waitMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        logger.LogInformation(
            "Acquired the exclusive GPU lease for workload {Workload} after {WaitMilliseconds:0.0} ms.",
            workloadName,
            waitMilliseconds);

        return new GpuResourceLease(this, workloadName);
    }

    private void Release(string workloadName)
    {
        semaphore.Release();
        logger.LogInformation(
            "Released the exclusive GPU lease for workload {Workload}.",
            workloadName);
    }

    private sealed class GpuResourceLease : IAsyncDisposable
    {
        private readonly string workloadName;
        private GpuResourceGate? gate;

        public GpuResourceLease(GpuResourceGate gate, string workloadName)
        {
            this.gate = gate;
            this.workloadName = workloadName;
        }

        public ValueTask DisposeAsync()
        {
            // Interlocked makes disposal idempotent: a double dispose releases once.
            var owner = Interlocked.Exchange(ref gate, null);
            owner?.Release(workloadName);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public static readonly NoopLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
