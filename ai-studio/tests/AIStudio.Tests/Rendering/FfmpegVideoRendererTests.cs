using AIStudio.Application.Rendering;
using AIStudio.Domain.Assets;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class FfmpegVideoRendererTests : IDisposable
{
    private readonly string root;

    public FfmpegVideoRendererTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-renderer-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_ProducesDeterministicOutputMetadata()
    {
        var narration = WriteNarration([1, 2, 3]);
        var outputBytes = new byte[] { 9, 8, 7, 6 };
        var processRunner = FakeFfmpeg.WritingOutput(outputBytes, durationSeconds: 12);
        var renderer = CreateRenderer(processRunner);

        var result = await renderer.RenderAsync(
            new VideoRenderRequest(
                [
                    new SceneMediaInput("/root/scene-0.png", AssetType.Image),
                    new SceneMediaInput("/root/scene-1.png", AssetType.Image)
                ],
                narration,
                "renders/out.mp4"),
            TestContext.Current.CancellationToken);

        Assert.Equal("renders/out.mp4", result.RelativePath);
        Assert.Equal(RenderVideoTestData.Hash(outputBytes), result.ContentHash);
        Assert.Equal(outputBytes.Length, result.ByteSize);
        Assert.Equal(12, result.DurationSeconds);
        Assert.Equal(1280, result.Width);
        Assert.Equal(720, result.Height);

        var rendersDirectory = Path.Combine(root, "renders");
        Assert.True(File.Exists(Path.Combine(rendersDirectory, "out.mp4")));
        Assert.Empty(Directory.GetFiles(rendersDirectory, "*.part"));
        Assert.Equal(3, processRunner.CallCount);
    }

    [Fact]
    public async Task RenderAsync_RejectsOutputPathEscapingArtifactRoot()
    {
        var processRunner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => CreateRenderer(processRunner).RenderAsync(
                new VideoRenderRequest(
                    [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
                    "/root/narration.wav",
                    "../escape.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_output_path_invalid", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task RenderAsync_RejectsEmptySceneList()
    {
        var processRunner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => CreateRenderer(processRunner).RenderAsync(
                new VideoRenderRequest([], "/root/narration.wav", "renders/out.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_scene_count_invalid", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task RenderAsync_ReportsRenderFailedOnNonZeroExit()
    {
        var narration = WriteNarration([1]);
        var renderer = CreateRenderer(FakeFfmpeg.FailingRender("synthetic ffmpeg failure"));

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => renderer.RenderAsync(
                new VideoRenderRequest(
                    [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
                    narration,
                    "renders/out.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_failed", exception.ErrorCode);
        Assert.Contains("synthetic ffmpeg failure", exception.Message);
    }

    [Fact]
    public async Task RenderAsync_ReportsProbeFailedWhenFfprobeFails()
    {
        var narration = WriteNarration([1]);
        var processRunner = new FakeProcessRunner(
            _ => new ProcessResult(1, string.Empty, "synthetic ffprobe failure"));

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => CreateRenderer(processRunner).RenderAsync(
                new VideoRenderRequest(
                    [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
                    narration,
                    "renders/out.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_probe_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_MapsProcessStartFailureToToolUnavailable()
    {
        var narration = WriteNarration([1]);
        var processRunner = new FakeProcessRunner(
            _ => throw new ProcessExecutionException(
                ProcessExecutionException.StartFailed,
                "tool not found"));

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => CreateRenderer(processRunner).RenderAsync(
                new VideoRenderRequest(
                    [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
                    narration,
                    "renders/out.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_tool_unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_MapsProcessTimeout()
    {
        var narration = WriteNarration([1]);
        var processRunner = new FakeProcessRunner(
            _ => throw new ProcessExecutionException(
                ProcessExecutionException.TimedOut,
                "timed out"));

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => CreateRenderer(processRunner).RenderAsync(
                new VideoRenderRequest(
                    [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
                    narration,
                    "renders/out.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_timeout", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_RejectsOutputWithoutAudioStream()
    {
        var narration = WriteNarration([1]);
        var processRunner = FakeFfmpeg.WritingOutput([5, 5], outputHasAudio: false);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => CreateRenderer(processRunner).RenderAsync(
                new VideoRenderRequest(
                    [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
                    narration,
                    "renders/out.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_output_invalid", exception.ErrorCode);
    }

    private FfmpegVideoRenderer CreateRenderer(IProcessRunner processRunner) =>
        new(
            Options.Create(new RenderingOptions()),
            Options.Create(new AssetStorageOptions { RootPath = root }),
            processRunner);

    private string WriteNarration(byte[] bytes)
    {
        var path = Path.Combine(root, "narration.wav");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
