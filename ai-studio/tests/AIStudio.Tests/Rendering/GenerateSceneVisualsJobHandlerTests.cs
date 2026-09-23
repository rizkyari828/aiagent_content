using AIStudio.Application.Assets;
using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Narration;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Application.Rendering.Visuals;
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
        Assert.All(result.Visuals, visual => Assert.Equal("AnimatedSvg", visual.Engine));
        Assert.All(result.Visuals, visual => Assert.Equal("None", visual.Template));
        Assert.Equal(2, svg.AnimatedBriefs.Count);
        Assert.Empty(svg.Briefs);
        Assert.Equal(2, assets.Assets.Count);
        Assert.All(assets.Assets, asset =>
        {
            Assert.Equal(AssetType.Video, asset.Type);
            Assert.Equal(AssetOrigin.Local, asset.Origin);
            Assert.Equal(SceneAssetProvenance.CurrentGeneratedVisualSource, asset.Source);
            Assert.StartsWith("AnimatedSvg", asset.Creator);
        });
        Assert.NotNull(result.Routing);
        Assert.Equal(2, result.Routing!.Count);
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
            visual => visual.Engine == "AnimatedSvg" && visual.Status == "generated");
        Assert.Single(manim.Calls);
        Assert.Single(svg.AnimatedBriefs);
        Assert.Empty(svg.Briefs);
        Assert.All(assets.Assets, asset => Assert.Equal(AssetType.Video, asset.Type));
        Assert.All(result.Visuals, visual => Assert.EndsWith(".mp4", visual.Path));
    }

    [Fact]
    public async Task Handler_UsesAiImageEngineForClosingSceneWithMotionTreatment()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.ClosingStoryboard);
        var assets = new RecordingAssetRepository();
        var svg = new StubSceneVisualRenderer();
        var aiImages = new StubImageGenerationProvider(isEnabled: true);
        var handler = CreateHandler(
            projectId,
            storyboard,
            assets,
            svg,
            imageProvider: aiImages);

        var json = await handler.ExecuteAsync(
            GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = GenerateSceneVisualsResult.Deserialize(json);
        Assert.Equal(1, result.GeneratedCount);
        Assert.All(result.Visuals, visual => Assert.Equal("AiImage", visual.Engine));
        Assert.All(result.Visuals, visual => Assert.EndsWith(".mp4", visual.Path));
        var request = Assert.Single(aiImages.Requests);
        Assert.False(string.IsNullOrWhiteSpace(request.Prompt));
        Assert.True(request.Seed >= 0);
        Assert.Empty(request.IdentityReferences);

        // A still alone is not a scene: the motion treatment overlay must run.
        Assert.Single(svg.MotionLabels);
        Assert.Empty(svg.Briefs);
        Assert.All(assets.Assets, asset =>
        {
            Assert.Equal(AssetType.Video, asset.Type);
            Assert.StartsWith("AiImage", asset.Creator);
        });
    }

    [Fact]
    public async Task Handler_PassesConcreteIdentityPinToImageRequest()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.ClosingStoryboard);
        var aiImages = new StubImageGenerationProvider(isEnabled: true);
        var handler = CreateHandler(
            projectId,
            storyboard,
            new RecordingAssetRepository(),
            new StubSceneVisualRenderer(),
            imageProvider: aiImages);

        await handler.ExecuteAsync(
            GenerateSceneVisualsTestData.Job(
                projectId,
                storyboard.Id,
                identityReferences: [Pin(1)]),
            TestContext.Current.CancellationToken);

        var reference = Assert.Single(Assert.Single(aiImages.Requests).IdentityReferences);
        Assert.Equal("student-01-reference", reference.AssetId.Value);
        Assert.Equal(1, reference.Version.Value);
    }

    [Fact]
    public async Task Handler_ReplayOfSamePayloadReusesSamePinnedIdentityReference()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.ClosingStoryboard);
        var aiImages = new StubImageGenerationProvider(isEnabled: true);
        var job = GenerateSceneVisualsTestData.Job(
            projectId,
            storyboard.Id,
            identityReferences: [Pin(1)]);

        // Two independent replays of the SAME persisted payload. The pin is carried
        // by the payload, not re-resolved, so a later approval cannot drift it.
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var handler = CreateHandler(
                projectId,
                storyboard,
                new RecordingAssetRepository(),
                new StubSceneVisualRenderer(),
                imageProvider: aiImages);
            await handler.ExecuteAsync(job, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, aiImages.Requests.Count);
        Assert.All(aiImages.Requests, request =>
        {
            var reference = Assert.Single(request.IdentityReferences);
            Assert.Equal("student-01-reference", reference.AssetId.Value);
            Assert.Equal(1, reference.Version.Value);
        });
    }

    [Fact]
    public async Task Handler_RejectsMoreThanOneIdentityReference()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.ClosingStoryboard);
        var handler = CreateHandler(
            projectId,
            storyboard,
            new RecordingAssetRepository(),
            new StubSceneVisualRenderer(),
            imageProvider: new StubImageGenerationProvider(isEnabled: true));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(
                    projectId,
                    storyboard.Id,
                    identityReferences:
                    [
                        Pin(1),
                        new PinnedIdentityAsset(
                            new AssetReferenceId("teacher-02-reference"),
                            new IdentityAssetVersion(1))
                    ]),
                TestContext.Current.CancellationToken));

        Assert.Equal("visual_identity_reference_count_unsupported", exception.ErrorCode);
    }

    [Fact]
    public void Handler_DoesNotDependOnIdentityAssetResolution()
    {
        var parameters = Assert
            .Single(typeof(GenerateSceneVisualsJobHandler).GetConstructors())
            .GetParameters();

        var forbidden = new[]
        {
            typeof(IIdentityAssetRegistry),
            typeof(IIdentityAssetResolver),
            typeof(IIdentityAssetStore)
        };

        Assert.DoesNotContain(
            parameters,
            parameter => forbidden.Contains(parameter.ParameterType));
    }

    [Fact]
    public async Task Handler_UsesThreeDEngineForOpeningSceneWhenEnabled()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.OpeningStoryboard);
        var assets = new RecordingAssetRepository();
        var svg = new StubSceneVisualRenderer();
        var threeD = new StubThreeDRenderingProvider(isEnabled: true);
        var handler = CreateHandler(
            projectId,
            storyboard,
            assets,
            svg,
            threeDRenderer: threeD);

        var json = await handler.ExecuteAsync(
            GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = GenerateSceneVisualsResult.Deserialize(json);
        var threeDVisual = Assert.Single(result.Visuals, visual => visual.Engine == "ThreeD");
        Assert.EndsWith(".mp4", threeDVisual.Path);
        Assert.Equal("LocalAiLaptop", threeDVisual.Template);
        var rendered = Assert.Single(threeD.Requests);
        Assert.Equal(SceneThreeDTemplate.LocalAiLaptop, rendered.Template);
        Assert.Equal(SceneVisualPlanner.AnimationDurationSeconds, rendered.DurationSeconds, 3);
        Assert.True(rendered.Seed >= 0);

        Assert.Empty(svg.Briefs);
        Assert.Empty(svg.AnimatedBriefs);
        Assert.Contains(
            assets.Assets,
            asset => asset.Creator!.StartsWith("ThreeD", StringComparison.Ordinal) && asset.Type == AssetType.Video);
    }

    [Fact]
    public async Task Handler_UsesNarrationDerivedDurationForAnimatedClips()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.AnimatedStoryboard);
        var parsed = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.AnimatedStoryboard);
        var assets = new RecordingAssetRepository();
        var svg = new StubSceneVisualRenderer();
        var manim = new StubManimSceneRenderer(isEnabled: true);
        var narrationBytes = new byte[] { 1, 2, 3 };
        WriteFile("narration.wav", narrationBytes);
        var narration = RenderVideoTestData.Narration(
            projectId,
            storyboard.Id,
            "narration.wav",
            narrationBytes);
        var handler = CreateHandler(
            projectId,
            storyboard,
            assets,
            svg,
            manim,
            new StubNarrationRepository(narration),
            FakeMediaInspector.Returning(
                new MediaInspection(24, true, true, false, 0, 0)));

        await handler.ExecuteAsync(
            GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var expected = SceneTiming.AllocateForScenes(
            parsed.Scenes.Select(scene => scene.Heading).ToArray(),
            parsed.Scenes.Select(scene => scene.Visual).ToArray(),
            [true, true],
            24);

        Assert.Single(manim.Calls);
        Assert.Equal(expected[0], manim.Calls[0].Parameters.DurationSeconds, 3);
        Assert.Single(svg.AnimationDurations);
        Assert.Equal(expected[1], svg.AnimationDurations[0], 3);
        Assert.NotEqual(
            SceneVisualPlanner.AnimationDurationSeconds,
            manim.Calls[0].Parameters.DurationSeconds);
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
            RecipeRegistry(),
            new RecordingAssetRepository(),
            new StubNarrationRepository(null),
            new LocalAssetFileStore(
                Options.Create(new AssetStorageOptions { RootPath = root })),
            new AudioProductionWorkspace(
                new LocalAssetFileStore(
                    Options.Create(new AssetStorageOptions { RootPath = root })),
                FakeMediaInspector.Returning(new MediaInspection(0, false, false, false, 0, 0))),
            new StubSceneVisualRenderer(),
            new StubManimSceneRenderer(isEnabled: false),
            new StubImageGenerationProvider(isEnabled: false),
            new StubThreeDRenderingProvider(isEnabled: false),
            FakeMediaInspector.Returning(new MediaInspection(0, false, false, false, 0, 0)),
            new AssetStubTimeProvider(AssetTestData.Now));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RegeneratesOnlySceneWhoseFingerprintChanged()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.AnimatedStoryboard);
        var assets = new RecordingAssetRepository();
        var svg = new StubSceneVisualRenderer();
        var manim = new StubManimSceneRenderer(isEnabled: true);
        var handler = CreateHandler(projectId, storyboard, assets, svg, manim);

        var first = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));
        Assert.Equal(2, first.GeneratedCount);

        // Simulate a changed plan for scene 1 only.
        var sceneOne = assets.Assets.Single(asset => asset.SceneIndex == 1);
        sceneOne.Replace(
            sceneOne.Type,
            sceneOne.Path,
            sceneOne.ByteSize,
            sceneOne.ContentHash,
            SceneAssetProvenance.CurrentGeneratedVisualSource,
            "AnimatedSvg+000000000000");

        var second = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, second.GeneratedCount);
        Assert.Equal(1, second.SkippedCount);
        Assert.Equal("reused", second.Routing!.Single(entry => entry.SceneIndex == 0).Status);
        Assert.Equal("generated", second.Routing!.Single(entry => entry.SceneIndex == 1).Status);
        Assert.Equal(1, second.Visuals[0].SceneIndex);
        Assert.Single(manim.Calls);
        Assert.Equal(2, svg.AnimatedBriefs.Count);
    }

    [Fact]
    public async Task Handler_RegeneratesLegacyGeneratedAssetInPlace()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var assets = new RecordingAssetRepository();
        assets.Add(RenderVideoTestData.SceneAsset(
            projectId,
            storyboard.Id,
            0,
            "visuals/old/scene_0.png",
            [1, 2, 3],
            source: SceneAssetProvenance.GeneratedVisualSource,
            creator: "SvgStill"));
        var handler = CreateHandler(
            projectId,
            storyboard,
            assets,
            new StubSceneVisualRenderer());

        var result = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal(2, result.GeneratedCount);
        Assert.Equal(2, assets.Assets.Count);
        var regenerated = assets.Assets.Single(asset => asset.SceneIndex == 0);
        Assert.Equal(SceneAssetProvenance.CurrentGeneratedVisualSource, regenerated.Source);
        Assert.StartsWith("AnimatedSvg", regenerated.Creator);
        Assert.Equal(AssetType.Video, regenerated.Type);
    }

    [Fact]
    public async Task Handler_ForceRegeneratesEvenManualAssets()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var assets = new RecordingAssetRepository();
        assets.Add(RenderVideoTestData.SceneAsset(
            projectId,
            storyboard.Id,
            0,
            "visuals/manual.png",
            [9, 9]));
        var svg = new StubSceneVisualRenderer();
        var handler = CreateHandler(projectId, storyboard, assets, svg);

        var normal = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));
        Assert.Equal(1, normal.GeneratedCount);

        var forced = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id, force: true),
                TestContext.Current.CancellationToken));

        Assert.Equal(2, forced.GeneratedCount);
        Assert.All(
            assets.Assets,
            asset => Assert.Equal(SceneAssetProvenance.CurrentGeneratedVisualSource, asset.Source));
    }

    [Fact]
    public async Task Handler_MotionComicRoutesGenericNarrativeScenesToAiImage()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.NarrativeStoryboard);
        var aiImages = new StubImageGenerationProvider(isEnabled: true);
        var svg = new StubSceneVisualRenderer();
        var handler = CreateHandler(
            projectId,
            storyboard,
            new RecordingAssetRepository(),
            svg,
            imageProvider: aiImages);

        var result = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(
                    projectId,
                    storyboard.Id,
                    productionRecipe: Recipe()),
                TestContext.Current.CancellationToken));

        Assert.Equal(4, result.GeneratedCount);
        Assert.All(result.Visuals, visual =>
        {
            Assert.Equal("AiImage", visual.Engine);
            Assert.Equal("AiImage", visual.IntendedEngine);
            Assert.Equal("Generic", visual.Intent);
        });
        Assert.All(
            result.Routing!,
            entry =>
            {
                Assert.Equal("AiImage", entry.IntendedEngine);
                Assert.Equal("AiImage", entry.Engine);
                Assert.False(string.IsNullOrWhiteSpace(entry.Status));
            });
        Assert.Equal(4, aiImages.Requests.Count);
        Assert.Equal(4, svg.MotionLabels.Count);
        Assert.Empty(svg.AnimatedBriefs);
    }

    [Fact]
    public async Task Handler_NoRecipeKeepsGenericNarrativeScenesOnAnimatedSvg()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.NarrativeStoryboard);
        var aiImages = new StubImageGenerationProvider(isEnabled: true);
        var svg = new StubSceneVisualRenderer();
        var handler = CreateHandler(
            projectId,
            storyboard,
            new RecordingAssetRepository(),
            svg,
            imageProvider: aiImages);

        var result = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.All(result.Visuals, visual => Assert.Equal("AnimatedSvg", visual.Engine));
        Assert.All(result.Visuals, visual => Assert.Equal("AnimatedSvg", visual.IntendedEngine));
        Assert.Empty(aiImages.Requests);
        Assert.Equal(4, svg.AnimatedBriefs.Count);
    }

    [Fact]
    public async Task Handler_TechExplainerRecipeKeepsGenericNarrativeScenesOnAnimatedSvg()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.NarrativeStoryboard);
        var aiImages = new StubImageGenerationProvider(isEnabled: true);
        var handler = CreateHandler(
            projectId,
            storyboard,
            new RecordingAssetRepository(),
            new StubSceneVisualRenderer(),
            imageProvider: aiImages);

        var result = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(
                    projectId,
                    storyboard.Id,
                    productionRecipe: Recipe("tech-explainer", 1)),
                TestContext.Current.CancellationToken));

        Assert.All(result.Visuals, visual => Assert.Equal("AnimatedSvg", visual.Engine));
        Assert.Empty(aiImages.Requests);
    }

    [Fact]
    public async Task Handler_MotionComicDoesNotForceTechnicalIntentsToAiImage()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.AnimatedStoryboard);
        var aiImages = new StubImageGenerationProvider(isEnabled: true);
        var manim = new StubManimSceneRenderer(isEnabled: true);
        var svg = new StubSceneVisualRenderer();
        var handler = CreateHandler(
            projectId,
            storyboard,
            new RecordingAssetRepository(),
            svg,
            manim,
            imageProvider: aiImages);

        var result = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(
                    projectId,
                    storyboard.Id,
                    productionRecipe: Recipe()),
                TestContext.Current.CancellationToken));

        Assert.Contains(
            result.Visuals,
            visual => visual.Engine == "ManimAnimation" && visual.Template == "LocalAiFlow");
        Assert.Contains(result.Visuals, visual => visual.Engine == "AnimatedSvg");
        Assert.DoesNotContain(result.Visuals, visual => visual.Engine == "AiImage");
        Assert.Empty(aiImages.Requests);
        Assert.Single(manim.Calls);
    }

    [Fact]
    public async Task Handler_MotionComicFallsBackToAnimatedSvgWhenAiImageDisabled()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.NarrativeStoryboard);
        var svg = new StubSceneVisualRenderer();
        var handler = CreateHandler(
            projectId,
            storyboard,
            new RecordingAssetRepository(),
            svg,
            imageProvider: new StubImageGenerationProvider(isEnabled: false));

        var result = GenerateSceneVisualsResult.Deserialize(
            await handler.ExecuteAsync(
                GenerateSceneVisualsTestData.Job(
                    projectId,
                    storyboard.Id,
                    productionRecipe: Recipe()),
                TestContext.Current.CancellationToken));

        Assert.All(result.Visuals, visual =>
        {
            Assert.Equal("AnimatedSvg", visual.Engine);
            Assert.Equal("AiImage", visual.IntendedEngine);
            Assert.Equal("fallback", visual.Status);
        });
        Assert.Equal(4, svg.AnimatedBriefs.Count);
    }

    [Fact]
    public async Task Handler_MotionComicPassesIdentityPinToImageRequest()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.NarrativeStoryboard);
        var aiImages = new StubImageGenerationProvider(isEnabled: true);
        var handler = CreateHandler(
            projectId,
            storyboard,
            new RecordingAssetRepository(),
            new StubSceneVisualRenderer(),
            imageProvider: aiImages);

        await handler.ExecuteAsync(
            GenerateSceneVisualsTestData.Job(
                projectId,
                storyboard.Id,
                identityReferences: [Pin(1)],
                productionRecipe: Recipe()),
            TestContext.Current.CancellationToken);

        Assert.Equal(4, aiImages.Requests.Count);
        Assert.All(aiImages.Requests, request =>
        {
            var reference = Assert.Single(request.IdentityReferences);
            Assert.Equal("student-01-reference", reference.AssetId.Value);
            Assert.Equal(1, reference.Version.Value);
        });
    }

    [Fact]
    public async Task Handler_ReplayUsesSameRecipeRoutingContext()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.NarrativeStoryboard);
        var job = GenerateSceneVisualsTestData.Job(
            projectId,
            storyboard.Id,
            productionRecipe: Recipe());

        // Two independent replays of the SAME persisted payload must route the same
        // way; the exact recipe version travels with the payload, never "latest".
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var aiImages = new StubImageGenerationProvider(isEnabled: true);
            var handler = CreateHandler(
                projectId,
                storyboard,
                new RecordingAssetRepository(),
                new StubSceneVisualRenderer(),
                imageProvider: aiImages);

            var result = GenerateSceneVisualsResult.Deserialize(
                await handler.ExecuteAsync(
                    job,
                    TestContext.Current.CancellationToken));

            Assert.All(result.Visuals, visual => Assert.Equal("AiImage", visual.Engine));
        }
    }

    [Fact]
    public void Handler_DoesNotDependOnAudioOrGpuGate()
    {
        var parameters = Assert
            .Single(typeof(GenerateSceneVisualsJobHandler).GetConstructors())
            .GetParameters();

        var forbidden = new[]
        {
            typeof(AIStudio.Application.Rendering.AudioGeneration.ISpeechSynthesisProvider),
            typeof(AIStudio.Application.Rendering.AudioGeneration.IMusicGenerationProvider),
            typeof(AIStudio.Application.Rendering.AudioMixing.IAudioMixer),
            typeof(IGpuResourceGate)
        };

        Assert.DoesNotContain(
            parameters,
            parameter => forbidden.Contains(parameter.ParameterType));
    }

    private static PinnedIdentityAsset Pin(int version) =>
        new(new AssetReferenceId("student-01-reference"), new IdentityAssetVersion(version));

    private static IProductionRecipeRegistry RecipeRegistry() =>
        new ProductionRecipeRegistry(SeedProductionRecipes.All);

    private static ProductionRecipeReference Recipe(string id = "motion-comic", int version = 1) =>
        new()
        {
            Id = new ProductionRecipeId(id),
            Version = new ProductionRecipeVersion(version)
        };

    private GenerateSceneVisualsJobHandler CreateHandler(
        Guid projectId,
        JobSnapshot? storyboard,
        IAssetRepository assets,
        StubSceneVisualRenderer svg,
        StubManimSceneRenderer? manim = null,
        INarrationRepository? narrations = null,
        IMediaInspector? mediaInspector = null,
        StubImageGenerationProvider? imageProvider = null,
        StubThreeDRenderingProvider? threeDRenderer = null,
        IProductionRecipeRegistry? recipes = null)
    {
        var inspector = mediaInspector
            ?? FakeMediaInspector.Returning(new MediaInspection(0, false, false, false, 0, 0));
        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var fileStore = new LocalAssetFileStore(storage);

        return new GenerateSceneVisualsJobHandler(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            recipes ?? RecipeRegistry(),
            assets,
            narrations ?? new StubNarrationRepository(null),
            fileStore,
            new AudioProductionWorkspace(fileStore, inspector),
            svg,
            manim ?? new StubManimSceneRenderer(isEnabled: false),
            imageProvider ?? new StubImageGenerationProvider(isEnabled: false),
            threeDRenderer ?? new StubThreeDRenderingProvider(isEnabled: false),
            inspector,
            new AssetStubTimeProvider(AssetTestData.Now));
    }

    private void WriteFile(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
