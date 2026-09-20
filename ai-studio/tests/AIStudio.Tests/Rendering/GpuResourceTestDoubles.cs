using AIStudio.Application.Rendering;

namespace AIStudio.Tests.Rendering;

/// <summary>No-op gate for tests that do not exercise GPU serialization.</summary>
internal sealed class NoopGpuResourceGate : IGpuResourceGate
{
    public static readonly NoopGpuResourceGate Instance = new();

    public ValueTask<IAsyncDisposable> AcquireAsync(
        string workloadName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAsyncDisposable>(NoopLease.Instance);
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public static readonly NoopLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// Records acquisition/release without blocking, so tests can assert that a
/// provider holds the shared lease across the correct lifecycle window.
/// </summary>
internal sealed class RecordingGpuResourceGate : IGpuResourceGate
{
    public int ActiveLeases { get; private set; }

    public int AcquireCount { get; private set; }

    public List<string> Events { get; } = [];

    public ValueTask<IAsyncDisposable> AcquireAsync(
        string workloadName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ActiveLeases++;
        AcquireCount++;
        Events.Add($"acquire:{workloadName}");
        return ValueTask.FromResult<IAsyncDisposable>(new Lease(this, workloadName));
    }

    private sealed class Lease : IAsyncDisposable
    {
        private RecordingGpuResourceGate? owner;
        private readonly string workloadName;

        public Lease(RecordingGpuResourceGate owner, string workloadName)
        {
            this.owner = owner;
            this.workloadName = workloadName;
        }

        public ValueTask DisposeAsync()
        {
            var gate = Interlocked.Exchange(ref owner, null);
            if (gate is not null)
            {
                gate.ActiveLeases--;
                gate.Events.Add($"release:{workloadName}");
            }

            return ValueTask.CompletedTask;
        }
    }
}
