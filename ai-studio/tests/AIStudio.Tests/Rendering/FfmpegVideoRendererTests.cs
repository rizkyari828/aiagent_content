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
    public async Task RenderAsync_RejectsOutputPathEscapingArtifactRoot()
    {
        var renderer = CreateRenderer();

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => renderer.RenderAsync(
                new VideoRenderRequest(
                    [new SceneMediaInput("/tmp/scene-0.png", AssetType.Image)],
                    "/tmp/narration.wav",
                    "../escape.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_output_path_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_RejectsEmptySceneList()
    {
        var renderer = CreateRenderer();

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => renderer.RenderAsync(
                new VideoRenderRequest([], "/tmp/narration.wav", "renders/out.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_scene_count_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_ReportsUnavailableToolWhenFfprobeMissing()
    {
        var narration = Path.Combine(root, "narration.wav");
        await File.WriteAllBytesAsync(
            narration,
            [1, 2, 3],
            TestContext.Current.CancellationToken);
        var renderer = new FfmpegVideoRenderer(
            Options.Create(new RenderingOptions
            {
                FfprobePath = "/nonexistent/ffprobe-aistudio-test"
            }),
            Options.Create(new AssetStorageOptions { RootPath = root }));

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => renderer.RenderAsync(
                new VideoRenderRequest(
                    [new SceneMediaInput("/tmp/scene-0.png", AssetType.Image)],
                    narration,
                    "renders/out.mp4"),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_tool_unavailable", exception.ErrorCode);
    }

    private FfmpegVideoRenderer CreateRenderer() =>
        new(
            Options.Create(new RenderingOptions()),
            Options.Create(new AssetStorageOptions { RootPath = root }));
}
