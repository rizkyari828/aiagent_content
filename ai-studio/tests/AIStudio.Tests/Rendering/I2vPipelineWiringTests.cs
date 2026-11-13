using AIStudio.Application.Assets;
using AIStudio.Application.Bibles;
using AIStudio.Application.Capabilities;
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
using AIStudio.Infrastructure.Rendering;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using AIStudio.Tests.ProductionRecipes;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Tests covering I2V pipeline wiring per I2V_PLAN.md P3.3–P3.6:
/// - P3.3: Hero/narrative scene video asset generation + Creator fingerprint with engine + prompt + seed + first frame identity
/// - P3.4: VideoClipTiming integration into FfmpegCommandPlan
/// - P3.5: Fallback to Ken Burns (AiImage) when I2V is disabled or throws VideoGenerationException
/// - P3.6: Recipe motion-comic v2 resolution with VisualCharacterAnimation and fallback to VisualAiImage (v1 immutable)
/// </summary>
public sealed class I2vPipelineWiringTests : IDisposable
{
    private readonly string root;

    public I2vPipelineWiringTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-i2v-wiring-tests",
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
    public void P3_6_MotionComicV1_StaysImmutable()
    {
        var v1 = SeedProductionRecipes.All.Single(r => r.Id.Value == "motion-comic" && r.Version.Value == 1);
        Assert.Equal(1, v1.Version.Value);

        var visualRequirement = v1.Requirements.Single(r => r.Capability.CapabilityId.Equals(CapabilityIds.VisualAiImage));
        Assert.Equal([CapabilityIds.VisualStill], visualRequirement.Capability.FallbackCapabilityIds);
        Assert.True(visualRequirement.IsRequired);
    }

    [Fact]
    public void P3_6_MotionComicV2_ResolvesCharacterAnimationWithFallbackToAiImage()
    {
        var v2 = SeedProductionRecipes.All.Single(r => r.Id.Value == "motion-comic" && r.Version.Value == 2);
        Assert.Equal(2, v2.Version.Value);

        var visualRequirement = v2.Requirements.Single(r => r.Capability.CapabilityId.Equals(CapabilityIds.VisualCharacterAnimation));
        Assert.Equal([CapabilityIds.VisualAiImage], visualRequirement.Capability.FallbackCapabilityIds);
        Assert.True(visualRequirement.IsRequired);

        // When video is enabled, resolution is FullySupported
        var resolverVideoEnabled = new ProductionRecipeResolver(ProductionRecipeTestSupport.CapabilityRegistry(videoEnabled: true));
        var resEnabled = resolverVideoEnabled.Resolve(v2);
        Assert.Equal(ProductionRecipeStatus.FullySupported, resEnabled.Status);
        var resolvedVisual = resEnabled.Requirements.Single(r => r.Requirement.Capability.CapabilityId.Equals(CapabilityIds.VisualCharacterAnimation));
        Assert.False(resolvedVisual.Capability.UsedFallback);
        Assert.Equal(CapabilityIds.VisualCharacterAnimation, resolvedVisual.Capability.ResolvedCapabilityId);

        // When video is disabled, resolution falls back to VisualAiImage
        var resolverVideoDisabled = new ProductionRecipeResolver(ProductionRecipeTestSupport.CapabilityRegistry(videoEnabled: false));
        var resDisabled = resolverVideoDisabled.Resolve(v2);
        Assert.Equal(ProductionRecipeStatus.SupportedWithFallbacks, resDisabled.Status);
        var fallbackVisual = resDisabled.Requirements.Single(r => r.Requirement.Capability.CapabilityId.Equals(CapabilityIds.VisualCharacterAnimation));
        Assert.True(fallbackVisual.Capability.UsedFallback);
        Assert.Equal(CapabilityIds.VisualAiImage, fallbackVisual.Capability.ResolvedCapabilityId);
    }

    [Fact]
    public void P3_4_VideoClipTiming_LoopsShorterClipInFfmpegCommandPlan()
    {
        // 3s source video clip fitted to 7s scene duration -> VideoClipTiming gives RepeatCount = 2, target = 7.000
        var scenes = new[]
        {
            new SceneMediaInput("/assets/clip.mp4", AssetType.Video, SourceDurationSeconds: 3.0, DurationSeconds: 7.0)
        };

        var arguments = FfmpegCommandPlan.Build(
            scenes,
            "/root/narration.wav",
            "/root/out.mp4",
            new VideoRenderSettings(1280, 720, 24, 7.0, SceneTransition.Cut, 0.5, EnableMotion: true));

        var argList = arguments.ToList();
        var streamLoopIndex = argList.IndexOf("-stream_loop");
        Assert.True(streamLoopIndex >= 0, "Arguments must contain -stream_loop");
        Assert.Equal("2", argList[streamLoopIndex + 1]);

        var inputIndex = argList.IndexOf("/assets/clip.mp4");
        Assert.True(streamLoopIndex < inputIndex, "-stream_loop must precede the input video file");
        Assert.Equal("-t", argList[streamLoopIndex + 2]);
        Assert.Equal("7", argList[streamLoopIndex + 3]);
        Assert.Equal("-i", argList[streamLoopIndex + 4]);
        Assert.Equal("/assets/clip.mp4", argList[streamLoopIndex + 5]);
    }

    [Fact]
    public void P3_4_VideoClipTiming_TrimsLongerClipWithoutLoopInFfmpegCommandPlan()
    {
        // 5s source video clip fitted to 2s scene duration -> VideoClipTiming gives RepeatCount = 0, trimmedTail = 3.000
        var scenes = new[]
        {
            new SceneMediaInput("/assets/long-clip.mp4", AssetType.Video, SourceDurationSeconds: 5.0, DurationSeconds: 2.0)
        };

        var arguments = FfmpegCommandPlan.Build(
            scenes,
            "/root/narration.wav",
            "/root/out.mp4",
            new VideoRenderSettings(1280, 720, 24, 2.0, SceneTransition.Cut, 0.5, EnableMotion: true));

        // When RepeatCount is 0, -stream_loop must NOT be present
        Assert.DoesNotContain("-stream_loop", arguments);

        var argList = arguments.ToList();
        var tIndex = argList.IndexOf("-t");
        Assert.True(tIndex >= 0);
        Assert.Equal("2", argList[tIndex + 1]);
        Assert.Equal("-i", argList[tIndex + 2]);
        Assert.Equal("/assets/long-clip.mp4", argList[tIndex + 3]);
    }

    [Fact]
    public async Task P3_3_HeroVideoAssetGeneration_AndCreatorFingerprint()
    {
        var projectId = Guid.NewGuid();
        var storyboardJson = """
            {
              "title": "Anime Hero Storyboard",
              "scenes": [
                {
                  "heading": "Hero introduction",
                  "visual": "A young female student in uniform stands on the school rooftop as wind blows through her hair."
                }
              ]
            }
            """;
        var storyboard = AssetTestData.StoryboardJob(projectId, storyboardJson);
        var assets = new RecordingAssetRepository();
        var fakeVideo = new FakeVideoGenerationProvider
        {
            IsEnabled = true,
            Result = new VideoGenerationResult("renders/i2v/hero_scene_0.mp4", new string('c', 64), 2048, 4.0, 1280, 720, 24)
        };

        var pin = new PinnedIdentityAsset(new AssetReferenceId("student-01-reference"), new IdentityAssetVersion(1));
        var recipeRef = new ProductionRecipeReference
        {
            Id = new ProductionRecipeId("motion-comic"),
            Version = new ProductionRecipeVersion(2)
        };

        var handler = CreateHandler(projectId, storyboard, assets, fakeVideoProvider: fakeVideo);

        var payload = new GenerateSceneVisualsJobPayload(projectId, storyboard.Id, Force: true)
        {
            ProductionRecipe = recipeRef,
            IdentityReferences = [pin],
            ArtDirection = "dramatic cinematic anime lighting",
            VideoProvider = "comfyui-i2v"
        };
        var claimedJob = new ClaimedJob(
            Guid.NewGuid(),
            projectId,
            JobType.GenerateSceneVisuals,
            "hash",
            payload.Serialize(),
            0,
            2,
            false);

        var json = await handler.ExecuteAsync(claimedJob, TestContext.Current.CancellationToken);
        var result = GenerateSceneVisualsResult.Deserialize(json);

        Assert.Equal(1, result.SceneCount);
        Assert.Equal(1, result.GeneratedCount);
        Assert.Single(fakeVideo.Requests);

        var videoReq = fakeVideo.Requests[0];
        Assert.Contains("young female student in uniform", videoReq.Prompt);
        Assert.Equal(1280, videoReq.Width);
        Assert.Equal(720, videoReq.Height);
        Assert.Equal(24, videoReq.FramesPerSecond);
        Assert.Equal(pin, videoReq.FirstFrame);
        Assert.Equal("comfyui-i2v", videoReq.Provider);

        var registeredAsset = Assert.Single(assets.Assets);
        Assert.Equal(AssetType.Video, registeredAsset.Type);
        Assert.StartsWith("CharacterAnimation+", registeredAsset.Creator);

        var expectedCreator = SceneVisualFingerprint.CharacterAnimationCreator(videoReq.Prompt, videoReq.Seed, pin);
        Assert.Equal(expectedCreator, registeredAsset.Creator);

        Assert.Single(result.Visuals);
        Assert.Equal("CharacterAnimation", result.Visuals[0].Engine);
        Assert.Equal("generated", result.Visuals[0].Status);
    }

    [Fact]
    public async Task P3_5_FallbackToKenBurns_WhenI2vThrows()
    {
        var projectId = Guid.NewGuid();
        var storyboardJson = """
            {
              "title": "Anime Fallback Storyboard",
              "scenes": [
                {
                  "heading": "Hero introduction",
                  "visual": "A student standing in corridor looking surprised."
                }
              ]
            }
            """;
        var storyboard = AssetTestData.StoryboardJob(projectId, storyboardJson);
        var assets = new RecordingAssetRepository();
        var fakeVideo = new FakeVideoGenerationProvider
        {
            IsEnabled = true,
            Failure = new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "GPU out of memory on local ComfyUI worker")
        };
        var stubImage = new StubImageGenerationProvider(isEnabled: true);

        var recipeRef = new ProductionRecipeReference
        {
            Id = new ProductionRecipeId("motion-comic"),
            Version = new ProductionRecipeVersion(2)
        };

        var handler = CreateHandler(projectId, storyboard, assets, fakeVideoProvider: fakeVideo, imageProvider: stubImage);

        var payload = new GenerateSceneVisualsJobPayload(projectId, storyboard.Id, Force: true)
        {
            ProductionRecipe = recipeRef,
            ArtDirection = "cinematic lighting"
        };
        var claimedJob = new ClaimedJob(
            Guid.NewGuid(),
            projectId,
            JobType.GenerateSceneVisuals,
            "hash",
            payload.Serialize(),
            0,
            2,
            false);

        // Job must not fail when I2V throws!
        var json = await handler.ExecuteAsync(claimedJob, TestContext.Current.CancellationToken);
        var result = GenerateSceneVisualsResult.Deserialize(json);

        Assert.Equal(1, result.SceneCount);
        Assert.Equal(1, result.GeneratedCount);
        Assert.Single(stubImage.Requests);

        var registeredAsset = Assert.Single(assets.Assets);
        Assert.Equal(AssetType.Video, registeredAsset.Type);
        Assert.StartsWith("AiImage+", registeredAsset.Creator);

        var visual = Assert.Single(result.Visuals);
        Assert.Equal("AiImage", visual.Engine);
        Assert.Equal("CharacterAnimation", visual.IntendedEngine);
        Assert.Equal("fallback", visual.Status);

        var route = Assert.Single(result.Routing!);
        Assert.Equal("CharacterAnimation", route.IntendedEngine);
        Assert.Equal("AiImage", route.Engine);
        Assert.Equal("fallback", route.Status);
    }

    [Fact]
    public async Task P3_5_FallbackToKenBurns_WhenI2vDisabled()
    {
        var projectId = Guid.NewGuid();
        var storyboardJson = """
            {
              "title": "Anime Disabled Storyboard",
              "scenes": [
                {
                  "heading": "Hero introduction",
                  "visual": "A student standing at the chalkboard."
                }
              ]
            }
            """;
        var storyboard = AssetTestData.StoryboardJob(projectId, storyboardJson);
        var assets = new RecordingAssetRepository();
        var fakeVideo = new FakeVideoGenerationProvider
        {
            IsEnabled = false
        };
        var stubImage = new StubImageGenerationProvider(isEnabled: true);

        var recipeRef = new ProductionRecipeReference
        {
            Id = new ProductionRecipeId("motion-comic"),
            Version = new ProductionRecipeVersion(2)
        };

        var handler = CreateHandler(projectId, storyboard, assets, fakeVideoProvider: fakeVideo, imageProvider: stubImage);

        var payload = new GenerateSceneVisualsJobPayload(projectId, storyboard.Id, Force: true)
        {
            ProductionRecipe = recipeRef
        };
        var claimedJob = new ClaimedJob(
            Guid.NewGuid(),
            projectId,
            JobType.GenerateSceneVisuals,
            "hash",
            payload.Serialize(),
            0,
            2,
            false);

        var json = await handler.ExecuteAsync(claimedJob, TestContext.Current.CancellationToken);
        var result = GenerateSceneVisualsResult.Deserialize(json);

        Assert.Equal(1, result.SceneCount);
        Assert.Equal(1, result.GeneratedCount);
        Assert.Empty(fakeVideo.Requests);
        Assert.Single(stubImage.Requests);

        var registeredAsset = Assert.Single(assets.Assets);
        Assert.StartsWith("AiImage+", registeredAsset.Creator);

        var visual = Assert.Single(result.Visuals);
        Assert.Equal("AiImage", visual.Engine);
        Assert.Equal("CharacterAnimation", visual.IntendedEngine);
        Assert.Equal("fallback", visual.Status);
    }

    private GenerateSceneVisualsJobHandler CreateHandler(
        Guid projectId,
        JobSnapshot? storyboard,
        IAssetRepository assets,
        FakeVideoGenerationProvider? fakeVideoProvider = null,
        StubImageGenerationProvider? imageProvider = null,
        StubSceneVisualRenderer? svgRenderer = null)
    {
        var inspector = FakeMediaInspector.Returning(new MediaInspection(0, false, false, false, 0, 0));
        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var fileStore = new LocalAssetFileStore(storage);

        return new GenerateSceneVisualsJobHandler(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            new ProductionRecipeRegistry(SeedProductionRecipes.All),
            assets,
            new StubNarrationRepository(null),
            fileStore,
            new AudioProductionWorkspace(fileStore, inspector),
            svgRenderer ?? new StubSceneVisualRenderer(),
            new StubManimSceneRenderer(isEnabled: false),
            imageProvider ?? new StubImageGenerationProvider(isEnabled: false),
            new StubThreeDRenderingProvider(isEnabled: false),
            inspector,
            new AssetStubTimeProvider(AssetTestData.Now),
            fakeVideoProvider);
    }
}
