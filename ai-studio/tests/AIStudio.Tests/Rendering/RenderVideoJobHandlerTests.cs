using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Rendering;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Narration;
using AIStudio.Domain.Subtitles;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using AIStudio.Tests.Subtitles;
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
        var outputBytes = new byte[] { 42, 42, 42 };
        var processRunner = FakeFfmpeg.WritingOutput(outputBytes);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            narration,
            processRunner);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Equal($"renders/{projectId:N}/{storyboard.Id:N}.mp4", result.OutputPath);
        Assert.Equal(RenderVideoTestData.Hash(outputBytes), result.ContentHash);
        Assert.Equal(outputBytes.Length, result.ByteSize);
        Assert.Equal(2, result.SceneCount);
        Assert.Equal(storyboard.Id, result.StoryboardJobId);
        Assert.Equal(narration.Id, result.NarrationTrackId);
        Assert.Equal(RenderVideoTestData.Hash(narrationBytes), result.NarrationContentHash);
        Assert.True(File.Exists(Path.Combine(
            root,
            "renders",
            projectId.ToString("N"),
            $"{storyboard.Id:N}.mp4")));
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
        var processRunner = new FakeProcessRunner();
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
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_asset_hash_mismatch", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
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
        var processRunner = new FakeProcessRunner();
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
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_narration_hash_mismatch", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsMissingAssetFile()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var processRunner = new FakeProcessRunner();
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "missing.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "also-missing.png", [2])
            ],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", [3]),
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_asset_unreadable", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsIncompleteSceneAssets()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var processRunner = new FakeProcessRunner();
        WriteFile("scene-0.png", [1]);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", [1])],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", [3]),
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_assets_incomplete", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsMissingNarration()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var processRunner = new FakeProcessRunner();
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
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_narration_not_found", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_MapsNonZeroRendererExitToJobErrorCode()
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
        var processRunner = FakeFfmpeg.FailingRender("synthetic failure");
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
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_failed", exception.ErrorCode);
        Assert.Equal(2, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_IncludesMatchingSubtitle()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        var narrationBytes = new byte[] { 3 };
        var subtitleBytes = "1\n00:00:00,000 --> 00:00:02,000\nHello\n"u8.ToArray();
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        WriteFile("subtitle.srt", subtitleBytes);
        var subtitle = SubtitleTrack.Create(
            projectId,
            storyboard.Id,
            "subtitle.srt",
            subtitleBytes.Length,
            RenderVideoTestData.Hash(subtitleBytes),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now);
        var processRunner = FakeFfmpeg.WritingOutput([42, 42]);
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
            processRunner,
            subtitle);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Equal(subtitle.Id, result.SubtitleTrackId);
        Assert.Equal(RenderVideoTestData.Hash(subtitleBytes), result.SubtitleContentHash);

        var ffmpegArguments = processRunner.Requests[1].Arguments;
        Assert.Contains(Path.Combine(root, "subtitle.srt"), ffmpegArguments);
        Assert.Contains("mov_text", ffmpegArguments);
    }

    [Fact]
    public async Task Handler_RejectsChangedSubtitleHash()
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
        WriteFile("subtitle.srt", [9, 9]);
        var subtitle = SubtitleTrack.Create(
            projectId,
            storyboard.Id,
            "subtitle.srt",
            2,
            new string('d', SubtitleTrack.ContentHashLength),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now);
        var processRunner = new FakeProcessRunner();
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
            processRunner,
            subtitle);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_subtitle_hash_mismatch", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_IgnoresSubtitleFromAnotherStoryboard()
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
        WriteFile("subtitle.srt", [9]);
        var subtitle = SubtitleTrack.Create(
            projectId,
            Guid.NewGuid(),
            "subtitle.srt",
            1,
            RenderVideoTestData.Hash([9]),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now);
        var processRunner = FakeFfmpeg.WritingOutput([42]);
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
            processRunner,
            subtitle);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Null(result.SubtitleTrackId);
        Assert.Null(result.SubtitleContentHash);
        Assert.DoesNotContain("mov_text", processRunner.Requests[1].Arguments);
    }

    private RenderVideoJobHandler CreateHandler(
        Guid projectId,
        JobSnapshot storyboard,
        IReadOnlyList<SceneAsset> assets,
        NarrationTrack? narration,
        IProcessRunner processRunner,
        SubtitleTrack? subtitle = null) =>
        new(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            new StubAssetRepository([.. assets]),
            new StubNarrationRepository(narration),
            new StubSubtitleRepository(subtitle),
            new LocalAssetFileStore(
                Options.Create(new AssetStorageOptions { RootPath = root })),
            new FfmpegVideoRenderer(
                Options.Create(new RenderingOptions()),
                Options.Create(new AssetStorageOptions { RootPath = root }),
                processRunner,
                new FfprobeMediaInspector(
                    Options.Create(new RenderingOptions()),
                    processRunner)));

    private void WriteFile(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
