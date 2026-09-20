using AIStudio.Application.Rendering;
using AIStudio.Infrastructure.Gpu;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GpuResourceGateTests
{
    [Fact]
    public async Task FirstCallerAcquiresImmediately()
    {
        var gate = CreateGate();

        await using var lease = await gate.AcquireAsync(
            GpuWorkloads.ComfyUi,
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
    }

    [Fact]
    public async Task SecondCallerWaitsUntilFirstLeaseIsDisposed()
    {
        var token = TestContext.Current.CancellationToken;
        var gate = CreateGate();
        var first = await gate.AcquireAsync(GpuWorkloads.ComfyUi, token);

        var second = gate.AcquireAsync(GpuWorkloads.Blender, token).AsTask();
        Assert.False(second.IsCompleted);

        await first.DisposeAsync();

        var secondLease = await second.WaitAsync(TimeSpan.FromSeconds(5), token);
        await secondLease.DisposeAsync();
    }

    [Fact]
    public async Task CrossWorkloadHeavySectionsCannotOverlap()
    {
        var token = TestContext.Current.CancellationToken;
        var gate = CreateGate();
        var comfyLease = await gate.AcquireAsync(GpuWorkloads.ComfyUi, token);

        var blender = gate.AcquireAsync(GpuWorkloads.Blender, token).AsTask();
        var ollama = gate.AcquireAsync(GpuWorkloads.Ollama, token).AsTask();
        Assert.False(blender.IsCompleted);
        Assert.False(ollama.IsCompleted);

        await comfyLease.DisposeAsync();

        var blenderLease = await blender.WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.False(ollama.IsCompleted);
        await blenderLease.DisposeAsync();

        var ollamaLease = await ollama.WaitAsync(TimeSpan.FromSeconds(5), token);
        await ollamaLease.DisposeAsync();
    }

    [Fact]
    public async Task CancellationWhileWaitingDoesNotCorruptTheGate()
    {
        var token = TestContext.Current.CancellationToken;
        var gate = CreateGate();
        var first = await gate.AcquireAsync(GpuWorkloads.ComfyUi, token);

        using var cancellation = new CancellationTokenSource();
#pragma warning disable xUnit1051 // This test deliberately cancels the waiting lease.
        var waiting = gate.AcquireAsync(GpuWorkloads.Blender, cancellation.Token).AsTask();
#pragma warning restore xUnit1051
        Assert.False(waiting.IsCompleted);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        await first.DisposeAsync();

        // The permit is intact: a fresh caller acquires normally.
        var next = await gate.AcquireAsync(GpuWorkloads.Blender, token)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), token);
        await next.DisposeAsync();
    }

    [Fact]
    public async Task ExceptionInsideGatedOperationReleasesTheGate()
    {
        var token = TestContext.Current.CancellationToken;
        var gate = CreateGate();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var lease = await gate.AcquireAsync(GpuWorkloads.Blender, token);
            throw new InvalidOperationException("synthetic provider failure");
        });

        var next = await gate.AcquireAsync(GpuWorkloads.Ollama, token)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), token);
        await next.DisposeAsync();
    }

    [Fact]
    public async Task DoubleDisposalReleasesExactlyOnce()
    {
        var token = TestContext.Current.CancellationToken;
        var gate = CreateGate();
        var lease = await gate.AcquireAsync(GpuWorkloads.ComfyUi, token);

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        var held = await gate.AcquireAsync(GpuWorkloads.Blender, token);
        var blocked = gate.AcquireAsync(GpuWorkloads.Ollama, token).AsTask();
        await Task.Delay(50, token);

        // A leaked extra permit would let the third caller through immediately.
        Assert.False(blocked.IsCompleted);

        await held.DisposeAsync();
        var blockedLease = await blocked.WaitAsync(TimeSpan.FromSeconds(5), token);
        await blockedLease.DisposeAsync();
    }

    [Fact]
    public async Task DisabledGateDoesNotSerialize()
    {
        var token = TestContext.Current.CancellationToken;
        var gate = CreateGate(enabled: false);

        var first = await gate.AcquireAsync(GpuWorkloads.ComfyUi, token);
        var second = await gate.AcquireAsync(GpuWorkloads.Blender, token);

        await first.DisposeAsync();
        await second.DisposeAsync();
    }

    private static GpuResourceGate CreateGate(bool enabled = true) =>
        new(
            Options.Create(new GpuResourceGateOptions { Enabled = enabled }),
            NullLogger<GpuResourceGate>.Instance);
}
