using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.Rendering;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GenerateSceneVisualsJobHandlerTests : IDisposable
{
    private readonly string root;

    public GenerateSceneVisualsJobHandlerTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-visual-handler-tests",
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
    public async Task Handler_GeneratesAndRegistersOneAssetPerScene()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var assets = new RecordingAssetRepository();
        var svg = new StubSceneVisualRenderer();
        var handler = CreateHandler(projectId, storyboard, assets, svg);

        var json = await handler.ExecuteAsync(
            GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = GenerateSceneVisualsResult.Deserialize(json);
        Assert.Equal(storyboard.Id, result.StoryboardJobId);
        Assert.Equal(2, result.SceneCount);
        Assert.Equal(2, result.GeneratedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.All(result.Visuals, visual => Assert.Equal("SvgStill", visual.Engine));
        Assert.All(result.Visuals, visual => Assert.Equal("None", visual.Template));
        Assert.Equal(2, svg.Briefs.Count);
        Assert.Equal(2, assets.Assets.Count);
        Assert.All(assets.Assets, asset =>
        {
            Assert.Equal(AssetType.Image, asset.Type);
            Assert.Equal(AssetOrigin.Local, asset.Origin);
            Assert.Equal(SceneAssetProvenance.GeneratedVisualSource, asset.Source);
            Assert.Equal("SvgStill", asset.Creator);
        });
        Assert.True(File.Exists(Path.Combine(root, result.Visuals[0].Path)));
    }

    [Fact]
    public async Task Handler_PreservesExistingAssetAndIsIdempotent()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var manualBytes = new byte[] { 9, 9, 9 };
        WriteFile("manual.png", manualBytes);
        var manual = RenderVideoTestData.SceneAsset(
            projectId,
            storyboard.Id,
            0,
            "manual.png",
            manualBytes);
        var assets = new RecordingAssetRepository();
        assets.Add(manual);
        var handler = CreateHandler(projectId, storyboard, assets, new StubSceneVisualRenderer());

        var json = await handler.ExecuteAsync(
            GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = GenerateSceneVisualsResult.Deserialize(json);
        Assert.Equal(1, result.GeneratedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(2, assets.Assets.Count);
        Assert.Equal("manual.png", assets.Assets.Single(asset => asset.SceneIndex == 0).Path);

        // Re-running skips every scene because each now has an asset.
        var rerun = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));
        Assert.Equal(0, rerun.GeneratedCount);
        Assert.Equal(2, rerun.SkippedCount);
        Assert.Equal(2, assets.Assets.Count);
    }

    [Fact]
    public async Task Handler_UsesAnimationEngineWhenEnabled()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.AnimatedStoryboard);
        var assets = new RecordingAssetRepository();
        var svg = new StubSceneVisualRenderer();
        var manim = new StubManimSceneRenderer(isEnabled: true);
        var handler = CreateHandler(projectId, storyboard, assets, svg, manim);

        var json = await handler.ExecuteAsync(
            GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = GenerateSceneVisualsResult.Deserialize(json);
        Assert.Equal(2, result.GeneratedCount);
        Assert.Contains(
            result.Visuals,
            visual => visual.Engine == "ManimAnimation" && visual.Template == "LocalAiFlow");
        Assert.Contains(
            result.Visuals,
            visual => visual.Engine == "ManimAnimation" && visual.Template == "ChatFlow");
        Assert.Equal(2, manim.Calls.Count);
        Assert.Empty(svg.Briefs);
        Assert.All(assets.Assets, asset => Assert.Equal(AssetType.Video, asset.Type));
        Assert.All(result.Visuals, visual => Assert.EndsWith(".mp4", visual.Path));
    }

    [Fact]
    public async Task Handler_MapsRendererFailureToJobErrorCode()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var assets = new RecordingAssetRepository();
        var svg = new StubSceneVisualRenderer(
            _ => throw new RenderVideoException("visual_render_failed", "synthetic"));
        var handler = CreateHandler(projectId, storyboard, assets, svg);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("visual_render_failed", exception.ErrorCode);
        Assert.Empty(assets.Assets);
    }

    [Fact]
    public async Task Handler_RejectsWrongJobType()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var handler = CreateHandler(
            projectId,
            storyboard,
            new RecordingAssetRepository(),
            new StubSceneVisualRenderer());

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(
                    projectId,
                    storyboard.Id,
                    JobType.FinalVideoQa),
                TestContext.Current.CancellationToken));

        Assert.Equal("visual_wrong_job_type", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsMissingStoryboard()
    {
        var projectId = Guid.NewGuid();
        var handler = CreateHandler(
            projectId,
            storyboard: null,
            new RecordingAssetRepository(),
            new StubSceneVisualRenderer());

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, Guid.NewGuid()),
                TestContext.Current.CancellationToken));

        Assert.Equal("visual_storyboard_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsMissingProject()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var handler = new GenerateSceneVisualsJobHandler(
            new StubContentProjectReader(null),
            new StubJobReader(storyboard),
            new RecordingAssetRepository(),
            new LocalAssetFileStore(
                Options.Create(new AssetStorageOptions { RootPath = root })),
            new StubSceneVisualRenderer(),
            new StubManimSceneRenderer(isEnabled: false),
            new AssetStubTimeProvider(AssetTestData.Now));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_not_found", exception.ErrorCode);
    }

    private GenerateSceneVisualsJobHandler CreateHandler(
        Guid projectId,
        JobSnapshot? storyboard,
        IAssetRepository assets,
        StubSceneVisualRenderer svg,
        StubManimSceneRenderer? manim = null) =>
        new(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            assets,
            new LocalAssetFileStore(
                Options.Create(new AssetStorageOptions { RootPath = root })),
            svg,
            manim ?? new StubManimSceneRenderer(isEnabled: false),
            new AssetStubTimeProvider(AssetTestData.Now));

    private void WriteFile(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
