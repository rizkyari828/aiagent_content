using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AIStudio.Application.Assets;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Tests.IdentityAssets;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Deterministic fake-HTTP coverage for the I2V scaffold. No network, no GPU, and no
/// model is contacted: every ComfyUI response is produced in-process.
/// </summary>
public sealed class ComfyUiVideoGenerationProviderTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static readonly byte[] Mp4 =
    [
        0x00, 0x00, 0x00, 0x18,
        0x66, 0x74, 0x79, 0x70,
        0x69, 0x73, 0x6F, 0x6D,
        0x00, 0x00, 0x02, 0x00
    ];

    private readonly string root =
        Path.Combine(Path.GetTempPath(), $"aistudio-video-{Guid.NewGuid():N}");

    private readonly string workflowPath =
        Path.Combine(Path.GetTempPath(), $"aistudio-comfy-video-{Guid.NewGuid():N}.json");

    private readonly string firstFrameWorkflowPath =
        Path.Combine(Path.GetTempPath(), $"aistudio-comfy-video-first-frame-{Guid.NewGuid():N}.json");

    public ComfyUiVideoGenerationProviderTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(workflowPath, Template, Encoding.UTF8);
        File.WriteAllText(firstFrameWorkflowPath, FirstFrameTemplate, Encoding.UTF8);
    }

    public void Dispose()
    {
        foreach (var path in new[] { workflowPath, firstFrameWorkflowPath })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_ThrowsVideoNotConfiguredWhenDisabled()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP must not be called")),
            enabled: false);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.NotConfigured, exception.ErrorCode);
        Assert.False(provider.IsEnabled);
    }

    [Theory]
    [InlineData("", 3, 24, 1280, 720, "video_request_invalid")]
    [InlineData("prompt", 6, 24, 1280, 720, "video_request_invalid")]
    [InlineData("prompt", 3, 60, 1280, 720, "video_request_invalid")]
    [InlineData("prompt", 3, 24, 1281, 720, "video_request_invalid")]
    [InlineData("prompt", 3, 24, 1280, 721, "video_request_invalid")]
    public async Task GenerateAsync_RejectsRequestsOutsideTrustedBounds(
        string prompt,
        int durationSeconds,
        int framesPerSecond,
        int width,
        int height,
        string expectedCode)
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP must not be called")),
            enabled: true);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            new VideoGenerationRequest(prompt, durationSeconds, framesPerSecond, width, height, 7),
            TestContext.Current.CancellationToken));

        Assert.Equal(expectedCode, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_SubmitsBoundedValuesAndPersistsClip()
    {
        string? submitted = null;
        var handler = SuccessHandler(inspect: async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/prompt")
            {
                submitted = await request.Content!.ReadAsStringAsync(cancellationToken);
            }
        });
        var provider = CreateProvider(handler, enabled: true);

        var result = await provider.GenerateAsync(
            Request(prompt: "a paper goose unfolding its wings", seed: 42),
            TestContext.Current.CancellationToken);

        Assert.NotNull(submitted);
        Assert.Contains("a paper goose unfolding its wings", submitted);
        Assert.Contains("\"width\":1280", submitted);
        Assert.Contains("\"height\":720", submitted);
        Assert.Contains("\"fps\":24", submitted);
        Assert.Contains("\"duration\":3", submitted);
        Assert.Contains("\"seed\":42", submitted);
        Assert.DoesNotContain("__PROMPT__", submitted);
        Assert.DoesNotContain("__REFERENCE_IMAGE__", submitted);

        // The request body must never leak filesystem or host-specific detail.
        Assert.DoesNotContain("/Users", submitted);
        Assert.DoesNotContain("/tmp", submitted);
        Assert.DoesNotContain("127.0.0.1", submitted);
        Assert.DoesNotContain("http", submitted);
        Assert.DoesNotContain(root, submitted);

        Assert.StartsWith("renders/i2v/i2v-", result.RelativeOutputPath);
        Assert.EndsWith(".mp4", result.RelativeOutputPath);
        Assert.Equal(Mp4.Length, result.ByteSize);
        Assert.Equal(Hash(Mp4), result.ContentHash);
        Assert.Equal(3, result.DurationSeconds);
        Assert.Equal(1280, result.Width);
        Assert.Equal(720, result.Height);
        Assert.Equal(24, result.FramesPerSecond);
        Assert.True(File.Exists(Path.Combine(root, result.RelativeOutputPath)));
    }

    [Fact]
    public async Task GenerateAsync_UsesApprovedPinnedFirstFrameOnly()
    {
        var fixture = ApprovedReference();
        string? uploadedFilename = null;
        string? submitted = null;
        var handler = SuccessHandler(inspect: async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/upload/image")
            {
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                uploadedFilename = multipart
                    .Single(part => part.Headers.ContentDisposition!.Name!.Trim('"') == "image")
                    .Headers.ContentDisposition!.FileName!.Trim('"');
            }

            if (request.RequestUri.AbsolutePath == "/prompt")
            {
                submitted = await request.Content!.ReadAsStringAsync(cancellationToken);
            }
        });
        var provider = CreateProvider(
            handler,
            enabled: true,
            registry: fixture.Registry,
            store: fixture.Store);

        var result = await provider.GenerateAsync(
            Request(firstFrame: fixture.Reference),
            TestContext.Current.CancellationToken);

        Assert.NotNull(uploadedFilename);
        Assert.StartsWith("aistudio-student-reference-v1-", uploadedFilename);
        Assert.DoesNotContain("/", uploadedFilename);
        Assert.DoesNotContain("..", uploadedFilename);
        Assert.Contains(uploadedFilename, submitted);
        Assert.DoesNotContain("__REFERENCE_IMAGE__", submitted);
        Assert.Equal([(fixture.Reference.AssetId, fixture.Reference.Version)], fixture.Store.Reads);
        Assert.EndsWith(".mp4", result.RelativeOutputPath);
    }

    [Fact]
    public async Task GenerateAsync_RejectsUnapprovedFirstFrameBeforeHttp()
    {
        var fixture = ApprovedReference(approve: false);
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            registry: fixture.Registry,
            store: fixture.Store);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(firstFrame: fixture.Reference),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.RequestInvalid, exception.ErrorCode);
        Assert.Empty(fixture.Store.Reads);
    }

    [Fact]
    public async Task GenerateAsync_MapsMissingWorkflowToNotConfigured()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            workflow: Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.NotConfigured, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_RejectsWorkflowThatDoesNotMatchPlaceholderContract()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            workflow: firstFrameWorkflowPath);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.NotConfigured, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsUnreachableServerToGenerationFailed()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new HttpRequestException("connection refused")),
            enabled: true);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.GenerationFailed, exception.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task GenerateAsync_MapsSubmitFailuresToGenerationFailed(
        HttpStatusCode status,
        string body)
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => Task.FromResult(Json(status, body))),
            enabled: true);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.GenerationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsExecutionErrorToGenerationFailed()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/prompt")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}"));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, ErrorHistory));
        });
        var provider = CreateProvider(handler, enabled: true);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.GenerationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsCompletedWithoutArtifactToGenerationFailed()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/prompt")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}"));
            }

            return Task.FromResult(Json(
                HttpStatusCode.OK,
                "{\"abc\":{\"status\":{\"completed\":true,\"status_str\":\"success\"},\"outputs\":{}}}"));
        });
        var provider = CreateProvider(handler, enabled: true);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.GenerationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsPollTimeoutToVideoTimeout()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/prompt")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}"));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, "{}"));
        });
        var provider = CreateProvider(handler, enabled: true, timeoutSeconds: 1);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.Timeout, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsNonVideoArtifactToArtifactInvalid()
    {
        var handler = SuccessHandler(artifact: Png);
        var provider = CreateProvider(handler, enabled: true);

        var exception = await Assert.ThrowsAsync<VideoGenerationException>(() => provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.ArtifactInvalid, exception.ErrorCode);
        Assert.Empty(Directory.GetFiles(root, "*.mp4", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task GenerateAsync_HoldsGpuLeaseAcrossGenerationAndReleasesIt()
    {
        var gate = new RecordingGpuResourceGate();
        var pollStarted = new TaskCompletionSource();
        var releasePoll = new TaskCompletionSource();
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/prompt")
            {
                return Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}");
            }

            if (path.StartsWith("/history/", StringComparison.Ordinal))
            {
                pollStarted.TrySetResult();
                await releasePoll.Task.WaitAsync(TestContext.Current.CancellationToken);
                return Json(HttpStatusCode.OK, History);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Mp4)
            };
        });
        var provider = CreateProvider(handler, enabled: true, gpuResourceGate: gate);

        var run = provider.GenerateAsync(Request(), TestContext.Current.CancellationToken);

        await pollStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, gate.ActiveLeases);
        Assert.Contains("acquire:comfyui", gate.Events);

        releasePoll.TrySetResult();
        await run;

        Assert.Equal(0, gate.ActiveLeases);
        Assert.Contains("release:comfyui", gate.Events);
    }

    private static VideoGenerationRequest Request(
        string prompt = "a calm local server room",
        int durationSeconds = 3,
        int framesPerSecond = 24,
        int width = 1280,
        int height = 720,
        long seed = 1,
        PinnedIdentityAsset? firstFrame = null) =>
        new(prompt, durationSeconds, framesPerSecond, width, height, seed, firstFrame);

    private ComfyUiVideoGenerationProvider CreateProvider(
        HttpMessageHandler handler,
        bool enabled,
        string? workflow = null,
        string? firstFrameWorkflow = null,
        int timeoutSeconds = 600,
        IGpuResourceGate? gpuResourceGate = null,
        IIdentityAssetRegistry? registry = null,
        IIdentityAssetStore? store = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8188/") },
            Options.Create(new ComfyUiVideoOptions
            {
                Enabled = enabled,
                BaseUrl = "http://127.0.0.1:8188",
                WorkflowPath = workflow ?? workflowPath,
                FirstFrameWorkflowPath = firstFrameWorkflow ?? firstFrameWorkflowPath,
                OutputDirectory = "renders/i2v",
                TimeoutSeconds = timeoutSeconds,
                MaxSeconds = 5,
                Width = 1280,
                Height = 720,
                Fps = 24
            }),
            gpuResourceGate ?? NoopGpuResourceGate.Instance,
            registry ?? new IdentityAssetRegistry(),
            store ?? new FakeIdentityAssetStore(),
            new LocalAssetFileStore(Options.Create(new AssetStorageOptions { RootPath = root })));

    private static StubHttpMessageHandler SuccessHandler(
        Func<HttpRequestMessage, CancellationToken, Task>? inspect = null,
        byte[]? artifact = null) =>
        new(async (request, cancellationToken) =>
        {
            if (inspect is not null)
            {
                await inspect(request, cancellationToken);
            }

            var path = request.RequestUri!.PathAndQuery;
            if (path == "/prompt")
            {
                return Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}");
            }

            if (path.StartsWith("/history/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, History);
            }

            if (path.StartsWith("/view?", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(artifact ?? Mp4)
                };
            }

            if (path == "/upload/image")
            {
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                var filename = multipart
                    .Single(part => part.Headers.ContentDisposition!.Name!.Trim('"') == "image")
                    .Headers.ContentDisposition!.FileName!.Trim('"');
                return Json(
                    HttpStatusCode.OK,
                    $"{{\"name\":\"{filename}\",\"subfolder\":\"\",\"type\":\"input\"}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

    private static ReferenceFixture ApprovedReference(bool approve = true)
    {
        var asset = IdentityAssetTestSupport.Asset(
            id: "student-reference",
            version: 1,
            kind: "reference-image",
            mediaType: "image/png") with
        {
            ByteSize = Png.Length,
            ContentHash = Hash(Png)
        };
        var registry = new IdentityAssetRegistry([asset]);
        if (approve)
        {
            registry.Approve(asset.Id, asset.Version);
        }

        var store = new FakeIdentityAssetStore();
        store.Set(asset.Id, asset.Version, Png);
        return new ReferenceFixture(
            registry,
            store,
            new PinnedIdentityAsset(asset.Id, asset.Version));
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private const string Template = """
        {
          "1": { "class_type": "TrustedTestPromptNode", "inputs": { "text": "__PROMPT__", "seed": "__SEED__" } },
          "2": {
            "class_type": "TrustedTestVideoNode",
            "inputs": {
              "width": "__WIDTH__",
              "height": "__HEIGHT__",
              "fps": "__FPS__",
              "duration": "__DURATION_SECONDS__"
            }
          }
        }
        """;

    private const string FirstFrameTemplate = """
        {
          "1": { "class_type": "TrustedTestPromptNode", "inputs": { "text": "__PROMPT__", "seed": "__SEED__" } },
          "2": {
            "class_type": "TrustedTestVideoNode",
            "inputs": {
              "width": "__WIDTH__",
              "height": "__HEIGHT__",
              "fps": "__FPS__",
              "duration": "__DURATION_SECONDS__"
            }
          },
          "3": { "class_type": "TrustedTestFrameNode", "inputs": { "image": "__REFERENCE_IMAGE__" } }
        }
        """;

    private const string History = """
        {
          "abc": {
            "status": { "completed": true, "status_str": "success" },
            "outputs": {
              "9": {
                "items": [
                  { "filename": "abc_00001_.mp4", "subfolder": "aistudio", "type": "output" }
                ]
              }
            }
          }
        }
        """;

    private const string ErrorHistory = """
        {
          "abc": {
            "status": { "completed": true, "status_str": "error" },
            "outputs": {}
          }
        }
        """;

    private sealed record ReferenceFixture(
        IdentityAssetRegistry Registry,
        FakeIdentityAssetStore Store,
        PinnedIdentityAsset Reference);

    private sealed class FakeIdentityAssetStore : IIdentityAssetStore
    {
        private readonly Dictionary<(AssetReferenceId, IdentityAssetVersion), byte[]> bytes = [];

        public List<(AssetReferenceId, IdentityAssetVersion)> Reads { get; } = [];

        public void Set(AssetReferenceId id, IdentityAssetVersion version, byte[] content) =>
            bytes[(id, version)] = content.ToArray();

        public Task<IdentityAssetBlob> WriteAsync(
            AssetReferenceId id,
            IdentityAssetVersion version,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            Set(id, version, content.ToArray());
            return Task.FromResult(new IdentityAssetBlob(content.Length, Hash(content.ToArray())));
        }

        public Task<ReadOnlyMemory<byte>> ReadBytesAsync(
            AssetReferenceId id,
            IdentityAssetVersion version,
            CancellationToken cancellationToken)
        {
            Reads.Add((id, version));
            if (!bytes.TryGetValue((id, version), out var content))
            {
                throw new FileNotFoundException();
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(content.ToArray());
        }

        public bool Exists(AssetReferenceId id, IdentityAssetVersion version) =>
            bytes.ContainsKey((id, version));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
