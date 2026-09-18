using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Rendering;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Narration;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class RenderVideoJobHandlerTests : IDisposable
{
    private readonly string root;

    public RenderVideoJobHandlerTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-render-handler-tests",
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
    public async Task Handler_RendersWhenInputsMatchRecordedHashes()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1, 2, 3 };
        var scene1 = new byte[] { 4, 5 };
        var narrationBytes = new byte[] { 6, 7, 8, 9 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        var narration = RenderVideoTestData.Narration(
            projectId,
            storyboard.Id,
            "narration.wav",
            narrationBytes);
        var renderer = new RecordingVideoRenderer();
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            narration,
            renderer);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Equal($"renders/{projectId:N}/{storyboard.Id:N}.mp4", result.OutputPath);
        Assert.Equal(2, result.SceneCount);
        Assert.Equal(storyboard.Id, result.StoryboardJobId);
        Assert.Equal(narration.Id, result.NarrationTrackId);
        Assert.Equal(RenderVideoTestData.Hash(narrationBytes), result.NarrationContentHash);
        Assert.Equal(1, renderer.CallCount);
        Assert.NotNull(renderer.Request);
        Assert.Equal(2, renderer.Request.Scenes.Count);
        Assert.StartsWith(root, renderer.Request.NarrationAbsolutePath);
    }

    [Fact]
    public async Task Handler_RejectsChangedAssetHash()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var bytes = new byte[] { 1, 2, 3 };
        WriteFile("scene-0.png", bytes);
        var renderer = new RecordingVideoRenderer();
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(
                    projectId,
                    storyboard.Id,
                    0,
                    "scene-0.png",
                    bytes,
                    contentHash: new string('a', 64)),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", [4])
            ],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", [5]),
            renderer);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_asset_hash_mismatch", exception.ErrorCode);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsChangedNarrationHash()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", [3, 3]);
        var renderer = new RecordingVideoRenderer();
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            RenderVideoTestData.Narration(
                projectId,
                storyboard.Id,
                "narration.wav",
                [3, 3],
                contentHash: new string('b', 64)),
            renderer);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_narration_hash_mismatch", exception.ErrorCode);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsMissingAssetFile()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var renderer = new RecordingVideoRenderer();
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "missing.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "also-missing.png", [2])
            ],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", [3]),
            renderer);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_asset_unreadable", exception.ErrorCode);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsIncompleteSceneAssets()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var renderer = new RecordingVideoRenderer();
        WriteFile("scene-0.png", [1]);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", [1])],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", [3]),
            renderer);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_assets_incomplete", exception.ErrorCode);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsMissingNarration()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var renderer = new RecordingVideoRenderer();
        WriteFile("scene-0.png", [1]);
        WriteFile("scene-1.png", [2]);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", [2])
            ],
            narration: null,
            renderer);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_narration_not_found", exception.ErrorCode);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task Handler_MapsRendererFailureToJobErrorCode()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        var narrationBytes = new byte[] { 3 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        var renderer = new RecordingVideoRenderer(
            failure: new RenderVideoException("render_failed", "FFmpeg failed."));
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            RenderVideoTestData.Narration(
                projectId,
                storyboard.Id,
                "narration.wav",
                narrationBytes),
            renderer);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_failed", exception.ErrorCode);
        Assert.Equal(1, renderer.CallCount);
    }

    private RenderVideoJobHandler CreateHandler(
        Guid projectId,
        JobSnapshot storyboard,
        IReadOnlyList<SceneAsset> assets,
        NarrationTrack? narration,
        IVideoRenderer renderer) =>
        new(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            new StubAssetRepository([.. assets]),
            new StubNarrationRepository(narration),
            new LocalAssetFileStore(
                Options.Create(new AssetStorageOptions { RootPath = root })),
            renderer);

    private void WriteFile(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
