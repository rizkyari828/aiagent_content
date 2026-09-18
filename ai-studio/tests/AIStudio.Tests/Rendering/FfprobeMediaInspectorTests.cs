using AIStudio.Application.Rendering;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class FfprobeMediaInspectorTests
{
    [Fact]
    public async Task InspectAsync_ParsesDurationStreamsAndDimensions()
    {
        const string json = """
            {"format":{"duration":"12.5"},"streams":[{"codec_type":"video","width":640,"height":480},{"codec_type":"audio"},{"codec_type":"subtitle"}]}
            """;
        var processRunner = new FakeProcessRunner(
            _ => new ProcessResult(0, json, string.Empty));
        var inspector = CreateInspector(processRunner);

        var inspection = await inspector.InspectAsync(
            "/tmp/video.mp4",
            TestContext.Current.CancellationToken);

        Assert.Equal(12.5, inspection.DurationSeconds);
        Assert.True(inspection.HasVideo);
        Assert.True(inspection.HasAudio);
        Assert.True(inspection.HasSubtitle);
        Assert.Equal(640, inspection.Width);
        Assert.Equal(480, inspection.Height);
        Assert.Equal("ffprobe", processRunner.Requests[0].FileName);
        Assert.Contains("-show_entries", processRunner.Requests[0].Arguments);
    }

    [Fact]
    public async Task InspectAsync_ReportsAbsentSubtitleStream()
    {
        const string json = """
            {"format":{"duration":"5"},"streams":[{"codec_type":"video","width":320,"height":240},{"codec_type":"audio"}]}
            """;
        var inspector = CreateInspector(
            new FakeProcessRunner(_ => new ProcessResult(0, json, string.Empty)));

        var inspection = await inspector.InspectAsync(
            "/tmp/video.mp4",
            TestContext.Current.CancellationToken);

        Assert.False(inspection.HasSubtitle);
        Assert.Equal(320, inspection.Width);
        Assert.Equal(240, inspection.Height);
    }

    [Fact]
    public async Task InspectAsync_ThrowsProbeFailedOnNonZeroExit()
    {
        var inspector = CreateInspector(
            new FakeProcessRunner(
                _ => new ProcessResult(1, string.Empty, "synthetic ffprobe failure")));

        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(
            () => inspector.InspectAsync(
                "/tmp/broken.mp4",
                TestContext.Current.CancellationToken));

        Assert.Equal(ProcessExecutionException.MediaProbeFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task InspectAsync_ThrowsProbeFailedOnInvalidJson()
    {
        var inspector = CreateInspector(
            new FakeProcessRunner(_ => new ProcessResult(0, "not-json", string.Empty)));

        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(
            () => inspector.InspectAsync(
                "/tmp/broken.mp4",
                TestContext.Current.CancellationToken));

        Assert.Equal(ProcessExecutionException.MediaProbeFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task InspectAsync_PropagatesProcessStartFailure()
    {
        var inspector = CreateInspector(
            new FakeProcessRunner(
                _ => throw new ProcessExecutionException(
                    ProcessExecutionException.StartFailed,
                    "tool not found")));

        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(
            () => inspector.InspectAsync(
                "/tmp/video.mp4",
                TestContext.Current.CancellationToken));

        Assert.Equal(ProcessExecutionException.StartFailed, exception.ErrorCode);
    }

    private static FfprobeMediaInspector CreateInspector(IProcessRunner processRunner) =>
        new(Options.Create(new RenderingOptions()), processRunner);
}
