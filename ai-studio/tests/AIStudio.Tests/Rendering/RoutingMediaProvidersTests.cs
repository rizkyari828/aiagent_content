using System.Text;
using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateAudio;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Narration;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Application.Scripts;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;
using AIStudio.Infrastructure;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Infrastructure.Rendering.AudioGeneration;
using AIStudio.Infrastructure.Rendering.Gemini;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class RoutingMediaProvidersTests
{
    // =========================================================================
    // Track A1: RoutingImageGenerationProvider tests
    // =========================================================================

    [Theory]
    [InlineData("comfyui-flux")]
    [InlineData("comfyui")]
    [InlineData("local")]
    [InlineData("COMFYUI-FLUX")]
    [InlineData("ComfyUi")]
    [InlineData("LOCAL")]
    public async Task ImageRouter_RoutesToComfyUi_WhenComfyTokensSpecified(string providerToken)
    {
        var comfySpy = new SpyImageProvider("comfy");
        var geminiSpy = new SpyImageProvider("gemini");
        var router = CreateImageRouter(comfySpy, geminiSpy, comfyEnabled: false, geminiEnabled: true, geminiApiKey: "key");

        var request = new ImageGenerationRequest("Generate an image", 12345, provider: providerToken);
        var bytes = await router.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("image-from-comfy", Encoding.UTF8.GetString(bytes));
        Assert.NotNull(comfySpy.LastRequest);
        Assert.Equal(providerToken, comfySpy.LastRequest.Provider);
        Assert.Null(geminiSpy.LastRequest);
    }

    [Theory]
    [InlineData("gemini-nanobanana")]
    [InlineData("gemini")]
    [InlineData("nanobanana")]
    [InlineData("GEMINI-NANOBANANA")]
    [InlineData("Gemini")]
    [InlineData("NANOBANANA")]
    public async Task ImageRouter_RoutesToGemini_WhenGeminiTokensSpecified(string providerToken)
    {
        var comfySpy = new SpyImageProvider("comfy");
        var geminiSpy = new SpyImageProvider("gemini");
        var router = CreateImageRouter(comfySpy, geminiSpy, comfyEnabled: true, geminiEnabled: false, geminiApiKey: null);

        var request = new ImageGenerationRequest("Generate an image", 12345, provider: providerToken);
        var bytes = await router.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("image-from-gemini", Encoding.UTF8.GetString(bytes));
        Assert.NotNull(geminiSpy.LastRequest);
        Assert.Equal(providerToken, geminiSpy.LastRequest.Provider);
        Assert.Null(comfySpy.LastRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImageRouter_FallsBackToComfy_WhenProviderOmittedAndComfyEnabled(string? providerToken)
    {
        var comfySpy = new SpyImageProvider("comfy");
        var geminiSpy = new SpyImageProvider("gemini");
        // Both enabled; comfy takes priority in fallback (!comfy.Enabled && gemini.Enabled -> gemini, else comfy)
        var router = CreateImageRouter(comfySpy, geminiSpy, comfyEnabled: true, geminiEnabled: true, geminiApiKey: "key");

        var request = new ImageGenerationRequest("Generate an image", 12345, provider: providerToken);
        var bytes = await router.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("image-from-comfy", Encoding.UTF8.GetString(bytes));
        Assert.NotNull(comfySpy.LastRequest);
        Assert.Null(geminiSpy.LastRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImageRouter_FallsBackToGemini_WhenProviderOmittedAndComfyDisabledAndGeminiConfigured(string? providerToken)
    {
        var comfySpy = new SpyImageProvider("comfy");
        var geminiSpy = new SpyImageProvider("gemini");
        var router = CreateImageRouter(comfySpy, geminiSpy, comfyEnabled: false, geminiEnabled: true, geminiApiKey: "valid-key");

        var request = new ImageGenerationRequest("Generate an image", 12345, provider: providerToken);
        var bytes = await router.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("image-from-gemini", Encoding.UTF8.GetString(bytes));
        Assert.NotNull(geminiSpy.LastRequest);
        Assert.Null(comfySpy.LastRequest);
    }

    [Fact]
    public async Task ImageRouter_FallsBackToComfy_WhenNeitherConfiguredOrMissingApiKey()
    {
        var comfySpy = new SpyImageProvider("comfy");
        var geminiSpy = new SpyImageProvider("gemini");
        var router = CreateImageRouter(comfySpy, geminiSpy, comfyEnabled: false, geminiEnabled: true, geminiApiKey: " ");

        var request = new ImageGenerationRequest("Generate an image", 12345, provider: null);
        var bytes = await router.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("image-from-comfy", Encoding.UTF8.GetString(bytes));
        Assert.NotNull(comfySpy.LastRequest);
        Assert.Null(geminiSpy.LastRequest);
    }

    [Theory]
    [InlineData("dall-e")]
    [InlineData("midjourney")]
    [InlineData("unknown")]
    public async Task ImageRouter_ThrowsRenderVideoException_WhenUnknownProviderToken(string unknownToken)
    {
        var comfySpy = new SpyImageProvider("comfy");
        var geminiSpy = new SpyImageProvider("gemini");
        var router = CreateImageRouter(comfySpy, geminiSpy, comfyEnabled: true, geminiEnabled: true, geminiApiKey: "key");

        var request = new ImageGenerationRequest("Generate an image", 12345, provider: unknownToken);
        var ex = await Assert.ThrowsAsync<RenderVideoException>(
            () => router.GenerateAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("image_provider_unknown", ex.ErrorCode);
        Assert.Contains(unknownToken, ex.Message);
    }

    [Fact]
    public void ImageRouter_IsEnabled_ReflectsDefaultFallback()
    {
        var comfySpy = new SpyImageProvider("comfy", isEnabled: false);
        var geminiSpy = new SpyImageProvider("gemini", isEnabled: true);

        // When comfy disabled and gemini enabled with key -> fallback is gemini
        var routerWithGemini = CreateImageRouter(comfySpy, geminiSpy, comfyEnabled: false, geminiEnabled: true, geminiApiKey: "key");
        Assert.True(routerWithGemini.IsEnabled);

        // When comfy enabled -> fallback is comfy
        var routerWithComfy = CreateImageRouter(comfySpy, geminiSpy, comfyEnabled: true, geminiEnabled: true, geminiApiKey: "key");
        Assert.False(routerWithComfy.IsEnabled);
    }

    // =========================================================================
    // Track A2: RoutingVideoGenerationProvider tests
    // =========================================================================

    [Theory]
    [InlineData("comfyui-i2v")]
    [InlineData("comfyui")]
    [InlineData("local")]
    [InlineData("COMFYUI-I2V")]
    [InlineData("ComfyUI")]
    [InlineData("LOCAL")]
    public async Task VideoRouter_RoutesToComfyUi_WhenComfyTokensSpecified(string providerToken)
    {
        var comfySpy = new SpyVideoProvider("comfy");
        var geminiSpy = new SpyVideoProvider("gemini");
        var router = CreateVideoRouter(comfySpy, geminiSpy, comfyEnabled: false, geminiEnabled: true, geminiApiKey: "key");

        var request = new VideoGenerationRequest("Render a scene", 5, 24, 1280, 720, 12345, Provider: providerToken);
        var result = await router.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("video-from-comfy.mp4", result.RelativeOutputPath);
        Assert.NotNull(comfySpy.LastRequest);
        Assert.Equal(providerToken, comfySpy.LastRequest.Provider);
        Assert.Null(geminiSpy.LastRequest);
    }

    [Theory]
    [InlineData("gemini-veo")]
    [InlineData("gemini")]
    [InlineData("veo")]
    [InlineData("GEMINI-VEO")]
    [InlineData("Gemini")]
    [InlineData("VEO")]
    public async Task VideoRouter_RoutesToGemini_WhenGeminiTokensSpecified(string providerToken)
    {
        var comfySpy = new SpyVideoProvider("comfy");
        var geminiSpy = new SpyVideoProvider("gemini");
        var router = CreateVideoRouter(comfySpy, geminiSpy, comfyEnabled: true, geminiEnabled: false, geminiApiKey: null);

        var request = new VideoGenerationRequest("Render a scene", 5, 24, 1280, 720, 12345, Provider: providerToken);
        var result = await router.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("video-from-gemini.mp4", result.RelativeOutputPath);
        Assert.NotNull(geminiSpy.LastRequest);
        Assert.Equal(providerToken, geminiSpy.LastRequest.Provider);
        Assert.Null(comfySpy.LastRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task VideoRouter_FallsBackToComfy_WhenProviderOmittedAndComfyEnabled(string? providerToken)
    {
        var comfySpy = new SpyVideoProvider("comfy");
        var geminiSpy = new SpyVideoProvider("gemini");
        var router = CreateVideoRouter(comfySpy, geminiSpy, comfyEnabled: true, geminiEnabled: true, geminiApiKey: "key");

        var request = new VideoGenerationRequest("Render a scene", 5, 24, 1280, 720, 12345, Provider: providerToken);
        var result = await router.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("video-from-comfy.mp4", result.RelativeOutputPath);
        Assert.NotNull(comfySpy.LastRequest);
        Assert.Null(geminiSpy.LastRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task VideoRouter_FallsBackToGemini_WhenProviderOmittedAndComfyDisabledAndGeminiConfigured(string? providerToken)
    {
        var comfySpy = new SpyVideoProvider("comfy");
        var geminiSpy = new SpyVideoProvider("gemini");
        var router = CreateVideoRouter(comfySpy, geminiSpy, comfyEnabled: false, geminiEnabled: true, geminiApiKey: "key");

        var request = new VideoGenerationRequest("Render a scene", 5, 24, 1280, 720, 12345, Provider: providerToken);
        var result = await router.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("video-from-gemini.mp4", result.RelativeOutputPath);
        Assert.NotNull(geminiSpy.LastRequest);
        Assert.Null(comfySpy.LastRequest);
    }

    [Theory]
    [InlineData("sora")]
    [InlineData("runway")]
    [InlineData("unknown-video")]
    public async Task VideoRouter_ThrowsVideoGenerationException_WhenUnknownProviderToken(string unknownToken)
    {
        var comfySpy = new SpyVideoProvider("comfy");
        var geminiSpy = new SpyVideoProvider("gemini");
        var router = CreateVideoRouter(comfySpy, geminiSpy, comfyEnabled: true, geminiEnabled: true, geminiApiKey: "key");

        var request = new VideoGenerationRequest("Render a scene", 5, 24, 1280, 720, 12345, Provider: unknownToken);
        var ex = await Assert.ThrowsAsync<VideoGenerationException>(
            () => router.GenerateAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.RequestInvalid, ex.ErrorCode);
        Assert.Contains(unknownToken, ex.Message);
    }

    [Fact]
    public void VideoRouter_IsEnabled_ReflectsDefaultFallback()
    {
        var comfySpy = new SpyVideoProvider("comfy", isEnabled: false);
        var geminiSpy = new SpyVideoProvider("gemini", isEnabled: true);

        var routerWithGemini = CreateVideoRouter(comfySpy, geminiSpy, comfyEnabled: false, geminiEnabled: true, geminiApiKey: "key");
        Assert.True(routerWithGemini.IsEnabled);

        var routerWithComfy = CreateVideoRouter(comfySpy, geminiSpy, comfyEnabled: true, geminiEnabled: true, geminiApiKey: "key");
        Assert.False(routerWithComfy.IsEnabled);
    }

    // =========================================================================
    // Track A3: RoutingSpeechSynthesisProvider tests
    // =========================================================================

    [Theory]
    [InlineData("voxcpm2")]
    [InlineData("voxcpm")]
    [InlineData("local")]
    [InlineData("VOXCPM2")]
    [InlineData("VoxCpm")]
    [InlineData("LOCAL")]
    public async Task SpeechRouter_RoutesToVoxCpm_WhenLocalTokensSpecified(string providerToken)
    {
        var localSpy = new SpySpeechProvider("local");
        var geminiSpy = new SpySpeechProvider("gemini");
        var router = CreateSpeechRouter(localSpy, geminiSpy, localEnabled: false, geminiEnabled: true, geminiApiKey: "key");

        var request = new SpeechSynthesisRequest("Narration text", SpeechVoiceProfile.Formal, "audio/test.wav", Provider: providerToken);
        var result = await router.SynthesizeAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("audio/test.wav", result.RelativePath);
        Assert.NotNull(localSpy.LastRequest);
        Assert.Equal(providerToken, localSpy.LastRequest.Provider);
        Assert.Null(geminiSpy.LastRequest);
    }

    [Theory]
    [InlineData("gemini-tts")]
    [InlineData("gemini")]
    [InlineData("GEMINI-TTS")]
    [InlineData("Gemini")]
    public async Task SpeechRouter_RoutesToGemini_WhenGeminiTokensSpecified(string providerToken)
    {
        var localSpy = new SpySpeechProvider("local");
        var geminiSpy = new SpySpeechProvider("gemini");
        var router = CreateSpeechRouter(localSpy, geminiSpy, localEnabled: true, geminiEnabled: false, geminiApiKey: null);

        var request = new SpeechSynthesisRequest("Narration text", SpeechVoiceProfile.Formal, "audio/test.wav", Provider: providerToken);
        var result = await router.SynthesizeAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("audio/test.wav", result.RelativePath);
        Assert.NotNull(geminiSpy.LastRequest);
        Assert.Equal(providerToken, geminiSpy.LastRequest.Provider);
        Assert.Null(localSpy.LastRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SpeechRouter_FallsBackToLocal_WhenProviderOmittedAndLocalEnabled(string? providerToken)
    {
        var localSpy = new SpySpeechProvider("local");
        var geminiSpy = new SpySpeechProvider("gemini");
        var router = CreateSpeechRouter(localSpy, geminiSpy, localEnabled: true, geminiEnabled: true, geminiApiKey: "key");

        var request = new SpeechSynthesisRequest("Narration text", SpeechVoiceProfile.Formal, "audio/test.wav", Provider: providerToken);
        var result = await router.SynthesizeAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(localSpy.LastRequest);
        Assert.Null(geminiSpy.LastRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SpeechRouter_FallsBackToGemini_WhenProviderOmittedAndLocalDisabledAndGeminiConfigured(string? providerToken)
    {
        var localSpy = new SpySpeechProvider("local");
        var geminiSpy = new SpySpeechProvider("gemini");
        var router = CreateSpeechRouter(localSpy, geminiSpy, localEnabled: false, geminiEnabled: true, geminiApiKey: "key");

        var request = new SpeechSynthesisRequest("Narration text", SpeechVoiceProfile.Formal, "audio/test.wav", Provider: providerToken);
        var result = await router.SynthesizeAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(geminiSpy.LastRequest);
        Assert.Null(localSpy.LastRequest);
    }

    [Theory]
    [InlineData("elevenlabs")]
    [InlineData("azure-speech")]
    [InlineData("unknown")]
    public async Task SpeechRouter_ThrowsSpeechSynthesisException_WhenUnknownProviderToken(string unknownToken)
    {
        var localSpy = new SpySpeechProvider("local");
        var geminiSpy = new SpySpeechProvider("gemini");
        var router = CreateSpeechRouter(localSpy, geminiSpy, localEnabled: true, geminiEnabled: true, geminiApiKey: "key");

        var request = new SpeechSynthesisRequest("Narration text", SpeechVoiceProfile.Formal, "audio/test.wav", Provider: unknownToken);
        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => router.SynthesizeAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("speech_provider_unknown", ex.ErrorCode);
        Assert.Contains(unknownToken, ex.Message);
    }

    [Fact]
    public void SpeechRouter_IsEnabled_ReflectsDefaultFallback()
    {
        var localSpy = new SpySpeechProvider("local", isEnabled: false);
        var geminiSpy = new SpySpeechProvider("gemini", isEnabled: true);

        var routerWithGemini = CreateSpeechRouter(localSpy, geminiSpy, localEnabled: false, geminiEnabled: true, geminiApiKey: "key");
        Assert.True(routerWithGemini.IsEnabled);

        var routerWithLocal = CreateSpeechRouter(localSpy, geminiSpy, localEnabled: true, geminiEnabled: true, geminiApiKey: "key");
        Assert.False(routerWithLocal.IsEnabled);
    }

    // =========================================================================
    // Track A4: Payload serialization, deserialization & backward compatibility
    // =========================================================================

    [Fact]
    public void GenerateSceneVisualsJobPayload_DeserializationBackwardCompatibility()
    {
        var projectId = Guid.NewGuid();
        var storyboardId = Guid.NewGuid();

        // Legacy JSON without imageProvider
        var legacyJson = $$"""
        {
            "contentProjectId": "{{projectId}}",
            "storyboardJobId": "{{storyboardId}}",
            "force": false
        }
        """;

        var payload = GenerateSceneVisualsJobPayload.Deserialize(legacyJson);
        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal(storyboardId, payload.StoryboardJobId);
        Assert.Null(payload.ImageProvider);

        // Modern JSON with imageProvider
        var modernJson = $$"""
        {
            "contentProjectId": "{{projectId}}",
            "storyboardJobId": "{{storyboardId}}",
            "force": true,
            "imageProvider": "gemini-nanobanana"
        }
        """;

        var modernPayload = GenerateSceneVisualsJobPayload.Deserialize(modernJson);
        Assert.Equal("gemini-nanobanana", modernPayload.ImageProvider);

        // Roundtrip serialization with imageProvider
        var serialized = JsonSerializer.Serialize(modernPayload);
        Assert.Contains("\"imageProvider\":\"gemini-nanobanana\"", serialized);

        // Roundtrip serialization without imageProvider omits it (WhenWritingNull)
        var serializedLegacy = JsonSerializer.Serialize(payload);
        Assert.DoesNotContain("imageProvider", serializedLegacy);
    }

    [Fact]
    public void GenerateAudioJobPayload_DeserializationBackwardCompatibility()
    {
        var projectId = Guid.NewGuid();
        var storyboardId = Guid.NewGuid();

        // Legacy JSON without speechProvider
        var legacyJson = $$"""
        {
            "contentProjectId": "{{projectId}}",
            "storyboardJobId": "{{storyboardId}}",
            "voiceProfile": "Formal",
            "locale": "id-ID"
        }
        """;

        var payload = GenerateAudioJobPayload.Deserialize(legacyJson);
        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal(storyboardId, payload.StoryboardJobId);
        Assert.Null(payload.SpeechProvider);

        // Modern JSON with speechProvider
        var modernJson = $$"""
        {
            "contentProjectId": "{{projectId}}",
            "storyboardJobId": "{{storyboardId}}",
            "voiceProfile": "Formal",
            "locale": "id-ID",
            "speechProvider": "gemini-tts"
        }
        """;

        var modernPayload = GenerateAudioJobPayload.Deserialize(modernJson);
        Assert.Equal("gemini-tts", modernPayload.SpeechProvider);

        // Roundtrip serialization with speechProvider
        var serialized = JsonSerializer.Serialize(modernPayload);
        Assert.Contains("\"speechProvider\":\"gemini-tts\"", serialized);

        // Roundtrip serialization without speechProvider omits it
        var serializedLegacy = JsonSerializer.Serialize(payload);
        Assert.DoesNotContain("speechProvider", serializedLegacy);
    }

    // =========================================================================
    // Track A5: Endpoint & Workflow Integration Tests
    // =========================================================================

    [Fact]
    public async Task VisualEndpoints_EnqueueVisualsAsync_ThreadsImageProviderToPayload()
    {
        var projectId = Guid.NewGuid();
        var storyboardJob = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var db = new RecordingDbContext();

        var workflow = new GenerateSceneVisualsWorkflow(
            db,
            new StubContentProjectReader(new ContentProjectSnapshot(projectId, "Test", "Brief")),
            new StubJobReader(storyboardJob),
            new ProductionRecipeRegistry(SeedProductionRecipes.All),
            new IdentityAssetResolver(new IdentityAssetRegistry()),
            new AssetStubTimeProvider(AssetTestData.Now));

        // 1. With imageProvider specified
        var requestWithProvider = new EnqueueVisualsRequest(
            IdentityReference: null,
            ProductionRecipe: null,
            ArtDirection: null,
            ImageProvider: "gemini-nanobanana");

        var result = await VisualEndpoints.EnqueueVisualsAsync(
            projectId.ToString(),
            storyboardJob.Id.ToString(),
            force: false,
            requestWithProvider,
            workflow,
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)result).StatusCode);
        var addedJob = db.AddedJob;
        Assert.NotNull(addedJob);
        Assert.Contains("\"imageProvider\":\"gemini-nanobanana\"", addedJob.Payload);

        // 2. Body-less request (request == null)
        var db2 = new RecordingDbContext();
        var workflow2 = new GenerateSceneVisualsWorkflow(
            db2,
            new StubContentProjectReader(new ContentProjectSnapshot(projectId, "Test", "Brief")),
            new StubJobReader(storyboardJob),
            new ProductionRecipeRegistry(SeedProductionRecipes.All),
            new IdentityAssetResolver(new IdentityAssetRegistry()),
            new AssetStubTimeProvider(AssetTestData.Now));

        var bodylessResult = await VisualEndpoints.EnqueueVisualsAsync(
            projectId.ToString(),
            storyboardJob.Id.ToString(),
            force: false,
            request: null,
            workflow2,
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)bodylessResult).StatusCode);
        var bodylessJob = db2.AddedJob;
        Assert.NotNull(bodylessJob);
        Assert.DoesNotContain("imageProvider", bodylessJob.Payload);
    }

    [Fact]
    public async Task AudioEndpoints_EnqueueAudioAsync_ThreadsSpeechProviderToPayload()
    {
        var projectId = Guid.NewGuid();
        var storyboardJob = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var db = new RecordingDbContext();

        var workflow = new GenerateAudioWorkflow(
            db,
            new StubContentProjectReader(new ContentProjectSnapshot(projectId, "Test", "Brief")),
            new StubJobReader(storyboardJob),
            new AssetStubTimeProvider(AssetTestData.Now));

        // 1. With speechProvider specified
        var requestWithProvider = new EnqueueAudioRequest("gemini-tts");
        var result = await AudioEndpoints.EnqueueAudioAsync(
            projectId.ToString(),
            storyboardJob.Id.ToString(),
            requestWithProvider,
            workflow,
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)result).StatusCode);
        var addedJob = db.AddedJob;
        Assert.NotNull(addedJob);
        Assert.Contains("\"speechProvider\":\"gemini-tts\"", addedJob.Payload);

        // 2. Body-less request (request == null)
        var db2 = new RecordingDbContext();
        var workflow2 = new GenerateAudioWorkflow(
            db2,
            new StubContentProjectReader(new ContentProjectSnapshot(projectId, "Test", "Brief")),
            new StubJobReader(storyboardJob),
            new AssetStubTimeProvider(AssetTestData.Now));

        var bodylessResult = await AudioEndpoints.EnqueueAudioAsync(
            projectId.ToString(),
            storyboardJob.Id.ToString(),
            request: null,
            workflow2,
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)bodylessResult).StatusCode);
        var bodylessJob = db2.AddedJob;
        Assert.NotNull(bodylessJob);
        Assert.DoesNotContain("speechProvider", bodylessJob.Payload);
    }

    // =========================================================================
    // Track A6: Handlers thread Provider into Provider Request
    // =========================================================================

    [Fact]
    public async Task GenerateSceneVisualsJobHandler_ThreadsImageProvider_IntoImageGenerationRequest()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateSceneVisualsTestData.ClosingStoryboard);

        var root = Path.Combine(Path.GetTempPath(), "aistudio-routing-visuals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var storage = Options.Create(new AssetStorageOptions { RootPath = root });
            var fileStore = new LocalAssetFileStore(storage);
            var workspace = new AudioProductionWorkspace(
                fileStore,
                FakeMediaInspector.Returning(new MediaInspection(0, false, false, false, 0, 0)));

            var imageSpy = new SpyImageProvider("image-spy");
            var handler = new GenerateSceneVisualsJobHandler(
                new StubContentProjectReader(new ContentProjectSnapshot(projectId, "Project", "Brief")),
                new StubJobReader(storyboard),
                new ProductionRecipeRegistry(SeedProductionRecipes.All),
                new RecordingAssetRepository(),
                new StubNarrationRepository(null),
                fileStore,
                workspace,
                new StubSceneVisualRenderer(),
                new StubManimSceneRenderer(isEnabled: false),
                imageSpy,
                new StubThreeDRenderingProvider(isEnabled: false),
                FakeMediaInspector.Returning(new MediaInspection(0, false, false, false, 0, 0)),
                new AssetStubTimeProvider(AssetTestData.Now));

            var payload = new GenerateSceneVisualsJobPayload(projectId, storyboard.Id)
            {
                ImageProvider = "gemini-nanobanana"
            };
            var job = new ClaimedJob(
                Guid.NewGuid(),
                projectId,
                JobType.GenerateSceneVisuals,
                "hash",
                JsonSerializer.Serialize(payload),
                0,
                2,
                false);

            await handler.ExecuteAsync(job, TestContext.Current.CancellationToken);

            Assert.NotNull(imageSpy.LastRequest);
            Assert.Equal("gemini-nanobanana", imageSpy.LastRequest.Provider);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task GenerateAudioJobHandler_ThreadsSpeechProvider_IntoSpeechSynthesisRequest()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);

        var root = Path.Combine(Path.GetTempPath(), "aistudio-routing-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var speechSpy = new SpySpeechProvider(root, "speech-spy");
            var storage = Options.Create(new AssetStorageOptions { RootPath = root });
            var fileStore = new LocalAssetFileStore(storage);
            var workspace = new AudioProductionWorkspace(fileStore, new FakeAudioInspector());

            var storyboardResult = GenerateStoryboardResult.Deserialize(storyboard.Result!);
            var scriptContent = JsonSerializer.Serialize(
                new
                {
                    title = storyboardResult.Title,
                    openingHook = string.Empty,
                    sections = storyboardResult.Scenes.Select(scene => new
                    {
                        heading = scene.Heading,
                        narration = $"Narration for {scene.Heading}."
                    }),
                    closing = string.Empty
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            var reviewed = GenerateStoryboardTestData.Script(
                projectId,
                ScriptReviewStatus.Approved,
                scriptContent);

            var handler = new GenerateAudioJobHandler(
                new StubContentProjectReader(new ContentProjectSnapshot(projectId, "Project", "Brief")),
                new StubJobReader(storyboard),
                new StubScriptReviewRepository(reviewed),
                fileStore,
                workspace,
                new FakeNarrationAssembler(),
                speechSpy,
                new FakeMusicGenerationProvider(root),
                new FakeAudioMixer(root));

            var payload = new GenerateAudioJobPayload(projectId, storyboard.Id)
            {
                SpeechProvider = "gemini-tts"
            };
            var job = new ClaimedJob(
                Guid.NewGuid(),
                projectId,
                JobType.GenerateAudio,
                "hash",
                JsonSerializer.Serialize(payload),
                0,
                2,
                false);

            await handler.ExecuteAsync(job, TestContext.Current.CancellationToken);

            Assert.NotNull(speechSpy.LastRequest);
            Assert.Equal("gemini-tts", speechSpy.LastRequest.Provider);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    // =========================================================================
    // Track A7: DependencyInjection service resolution
    // =========================================================================

    [Fact]
    public void DependencyInjection_ResolvesRoutingProviders()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=test;Username=test;Password=test",
            ["Ai:Provider"] = "Ollama",
            ["Ollama:BaseUrl"] = "http://127.0.0.1:11434",
            ["Ollama:DefaultModel"] = "test-model",
            ["Ollama:TimeoutSeconds"] = "30",
            ["GpuResourceGate:Enabled"] = "false",
            ["JobWorker:Enabled"] = "false",
            ["JobWorker:PollInterval"] = "00:00:01",
            ["JobWorker:LeaseDuration"] = "00:02:00"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var imageGen = provider.GetRequiredService<IImageGenerationProvider>();
        Assert.IsType<RoutingImageGenerationProvider>(imageGen);

        var videoGen = provider.GetRequiredService<IVideoGenerationProvider>();
        Assert.IsType<RoutingVideoGenerationProvider>(videoGen);

        var speechGen = provider.GetRequiredService<ISpeechSynthesisProvider>();
        Assert.IsType<RoutingSpeechSynthesisProvider>(speechGen);

        // Concrete services are also registered
        Assert.NotNull(provider.GetRequiredService<ComfyUiImageGenerationProvider>());
        Assert.NotNull(provider.GetRequiredService<GeminiImageGenerationProvider>());
        Assert.NotNull(provider.GetRequiredService<ComfyUiVideoGenerationProvider>());
        Assert.NotNull(provider.GetRequiredService<GeminiVideoGenerationProvider>());
        Assert.NotNull(provider.GetRequiredService<VoxCpmSpeechSynthesisProvider>());
        Assert.NotNull(provider.GetRequiredService<GeminiSpeechSynthesisProvider>());
    }

    // =========================================================================
    // Helper factories and test spies
    // =========================================================================

    private static RoutingImageGenerationProvider CreateImageRouter(
        IImageGenerationProvider comfySpy,
        IImageGenerationProvider geminiSpy,
        bool comfyEnabled,
        bool geminiEnabled,
        string? geminiApiKey)
    {
        var comfyOptions = Options.Create(new ComfyUiOptions { Enabled = comfyEnabled });
        var geminiOptions = Options.Create(new GeminiImageOptions { Enabled = geminiEnabled, ApiKey = geminiApiKey ?? string.Empty });
        return RoutingImageGenerationProvider.CreateForTesting(comfySpy, geminiSpy, comfyOptions, geminiOptions);
    }

    private static RoutingVideoGenerationProvider CreateVideoRouter(
        IVideoGenerationProvider comfySpy,
        IVideoGenerationProvider geminiSpy,
        bool comfyEnabled,
        bool geminiEnabled,
        string? geminiApiKey)
    {
        var comfyOptions = Options.Create(new ComfyUiVideoOptions { Enabled = comfyEnabled });
        var geminiOptions = Options.Create(new GeminiVideoOptions { Enabled = geminiEnabled, ApiKey = geminiApiKey ?? string.Empty });
        return RoutingVideoGenerationProvider.CreateForTesting(comfySpy, geminiSpy, comfyOptions, geminiOptions);
    }

    private static RoutingSpeechSynthesisProvider CreateSpeechRouter(
        ISpeechSynthesisProvider localSpy,
        ISpeechSynthesisProvider geminiSpy,
        bool localEnabled,
        bool geminiEnabled,
        string? geminiApiKey)
    {
        var localOptions = Options.Create(new SpeechSynthesisOptions { Enabled = localEnabled });
        var geminiOptions = Options.Create(new GeminiSpeechOptions { Enabled = geminiEnabled, ApiKey = geminiApiKey ?? string.Empty });
        return RoutingSpeechSynthesisProvider.CreateForTesting(localSpy, geminiSpy, localOptions, geminiOptions);
    }

    private sealed class SpyImageProvider(string name, bool isEnabled = true) : IImageGenerationProvider
    {
        public string Name { get; } = name;
        public bool IsEnabled { get; set; } = isEnabled;
        public ImageGenerationRequest? LastRequest { get; private set; }

        public Task<byte[]> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Encoding.UTF8.GetBytes($"image-from-{Name}"));
        }
    }

    private sealed class SpyVideoProvider(string name, bool isEnabled = true) : IVideoGenerationProvider
    {
        public string Name { get; } = name;
        public bool IsEnabled { get; set; } = isEnabled;
        public VideoGenerationRequest? LastRequest { get; private set; }

        public Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new VideoGenerationResult($"video-from-{Name}.mp4", "hash", 1024, 5, 1280, 720, 24));
        }
    }

    private sealed class SpySpeechProvider : ISpeechSynthesisProvider
    {
        private readonly string? root;

        public SpySpeechProvider(string name, bool isEnabled = true)
        {
            Name = name;
            IsEnabled = isEnabled;
        }

        public SpySpeechProvider(string root, string name, bool isEnabled = true)
        {
            this.root = root;
            Name = name;
            IsEnabled = isEnabled;
        }

        public string Name { get; }
        public bool IsEnabled { get; set; }
        public SpeechSynthesisRequest? LastRequest { get; private set; }

        public Task<SpeechSynthesisResult> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (root is not null)
            {
                var bytes = new byte[] { 1, 2, 3, 4 };
                FakeAudioFiles.Write(root, request.RelativeOutputPath, bytes);
            }
            return Task.FromResult(new SpeechSynthesisResult(
                request.RelativeOutputPath,
                "hash",
                1024,
                2.0,
                48000,
                request.Voice));
        }
    }
}
