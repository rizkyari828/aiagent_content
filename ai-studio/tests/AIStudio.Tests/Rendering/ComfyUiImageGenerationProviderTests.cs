using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Tests.IdentityAssets;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class ComfyUiImageGenerationProviderTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private readonly string workflowPath =
        Path.Combine(Path.GetTempPath(), $"aistudio-comfy-workflow-{Guid.NewGuid():N}.json");
    private readonly string editWorkflowPath =
        Path.Combine(Path.GetTempPath(), $"aistudio-comfy-edit-workflow-{Guid.NewGuid():N}.json");

    public ComfyUiImageGenerationProviderTests()
    {
        File.WriteAllText(workflowPath, Template, Encoding.UTF8);
        File.WriteAllText(editWorkflowPath, EditTemplate, Encoding.UTF8);
    }

    public void Dispose()
    {
        foreach (var path in new[] { workflowPath, editWorkflowPath })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task GenerateAsync_WithoutReferenceUsesOriginalWorkflowAndReturnsPng()
    {
        string? submitted = null;
        var uploadCalled = false;
        var handler = SuccessHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/upload/image")
            {
                uploadCalled = true;
            }

            if (request.RequestUri.AbsolutePath == "/prompt")
            {
                submitted = await request.Content!.ReadAsStringAsync(cancellationToken);
            }
        });
        var provider = CreateProvider(handler, enabled: true);

        var bytes = await provider.GenerateAsync(
            new ImageGenerationRequest("a calm local server room", 42),
            TestContext.Current.CancellationToken);

        Assert.Equal(Png, bytes);
        Assert.False(uploadCalled);
        Assert.NotNull(submitted);
        Assert.Contains("a calm local server room", submitted);
        Assert.Contains("\"width\":1280", submitted);
        Assert.Contains("\"height\":720", submitted);
        Assert.Contains("\"noise_seed\":42", submitted);
        Assert.Contains("trusted-text-model.safetensors", submitted);
        Assert.DoesNotContain("trusted-edit-model.safetensors", submitted);
        Assert.DoesNotContain("__PROMPT__", submitted);
    }

    [Fact]
    public async Task GenerateAsync_WithApprovedReferenceUploadsExactBytesAndUsesEditWorkflow()
    {
        var fixture = ApprovedReference();
        const string prompt = "keep identity; \\\"}], \\\"unet_name\\\":\\\"evil.safetensors";
        byte[]? uploadedBytes = null;
        string? uploadedFilename = null;
        string? uploadType = null;
        string? submitted = null;

        var handler = SuccessHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/upload/image")
            {
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (name == "image")
                    {
                        uploadedBytes = await part.ReadAsByteArrayAsync(cancellationToken);
                        uploadedFilename = part.Headers.ContentDisposition.FileName!.Trim('"');
                        Assert.Equal("image/png", part.Headers.ContentType!.MediaType);
                    }
                    else if (name == "type")
                    {
                        uploadType = await part.ReadAsStringAsync(cancellationToken);
                    }
                }
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
            new ImageGenerationRequest(prompt, 77, [fixture.Reference]),
            TestContext.Current.CancellationToken);

        Assert.Equal(Png, result);
        Assert.Equal(Png, uploadedBytes);
        Assert.Equal("input", uploadType);
        Assert.Equal("aistudio-student-reference-v1-" + Hash(Png)[..8] + ".png", uploadedFilename);
        Assert.DoesNotContain("/", uploadedFilename);
        Assert.DoesNotContain("..", uploadedFilename);
        Assert.Equal([(fixture.Reference.AssetId, fixture.Reference.Version)], fixture.Store.Reads);

        var body = JsonNode.Parse(submitted!)!.AsObject();
        var graph = body["prompt"]!.AsObject();
        Assert.Equal(prompt, graph["1"]!["inputs"]!["text"]!.GetValue<string>());
        Assert.Equal(77UL, graph["2"]!["inputs"]!["noise_seed"]!.GetValue<ulong>());
        Assert.Equal(uploadedFilename, graph["3"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.Equal("trusted-edit-model.safetensors", graph["4"]!["inputs"]!["unet_name"]!.GetValue<string>());
        Assert.DoesNotContain("trusted-text-model.safetensors", submitted);
        Assert.DoesNotContain("__REFERENCE_IMAGE__", submitted);
    }

    [Theory]
    [InlineData("draft", "reference-image", "image/png", "identity_asset_not_approved")]
    [InlineData("approved", "voice-reference", "image/png", "identity_asset_unsupported_kind")]
    [InlineData("approved", "reference-image", "image/jpeg", "identity_asset_unsupported_media_type")]
    public async Task GenerateAsync_RejectsInvalidReferenceMetadataBeforeHttp(
        string status,
        string kind,
        string mediaType,
        string expectedCode)
    {
        var fixture = ApprovedReference(kind: kind, mediaType: mediaType, approve: status == "approved");
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            registry: fixture.Registry,
            store: fixture.Store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [fixture.Reference]),
            TestContext.Current.CancellationToken));

        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Empty(fixture.Store.Reads);
    }

    [Fact]
    public async Task GenerateAsync_RejectsMissingExactMetadataWithoutLatestFallback()
    {
        var fixture = ApprovedReference(version: 2);
        var missingPin = new PinnedIdentityAsset(
            fixture.Reference.AssetId,
            new IdentityAssetVersion(1));
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            registry: fixture.Registry,
            store: fixture.Store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [missingPin]),
            TestContext.Current.CancellationToken));

        Assert.Equal("identity_asset_not_found", exception.ErrorCode);
        Assert.Empty(fixture.Store.Reads);
    }

    [Fact]
    public async Task GenerateAsync_ReadsRequestedVersionAndNeverLatestApproved()
    {
        var firstBytes = Png;
        var secondBytes = Png.Concat(new byte[] { 4, 5, 6 }).ToArray();
        var first = Asset(version: 1, bytes: firstBytes);
        var second = Asset(version: 2, bytes: secondBytes);
        var registry = new IdentityAssetRegistry([first, second]);
        registry.Approve(first.Id, first.Version);
        registry.Approve(second.Id, second.Version);
        var store = new FakeIdentityAssetStore();
        store.Set(first.Id, first.Version, firstBytes);
        store.Set(second.Id, second.Version, secondBytes);
        byte[]? uploaded = null;
        var handler = SuccessHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/upload/image")
            {
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                uploaded = await multipart.Single(part =>
                        part.Headers.ContentDisposition!.Name!.Trim('"') == "image")
                    .ReadAsByteArrayAsync(cancellationToken);
            }
        });
        var provider = CreateProvider(handler, true, registry: registry, store: store);

        await provider.GenerateAsync(
            new ImageGenerationRequest(
                "prompt",
                1,
                [new PinnedIdentityAsset(first.Id, first.Version)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(firstBytes, uploaded);
        Assert.Equal([(first.Id, first.Version)], store.Reads);
        Assert.DoesNotContain((second.Id, second.Version), store.Reads);
    }

    [Fact]
    public async Task GenerateAsync_MapsMissingBytesClearly()
    {
        var fixture = ApprovedReference(includeBytes: false);
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP must not be called")),
            true,
            registry: fixture.Registry,
            store: fixture.Store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [fixture.Reference]),
            TestContext.Current.CancellationToken));

        Assert.Equal("identity_asset_bytes_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_RejectsBytesThatDoNotMatchApprovedMetadata()
    {
        var fixture = ApprovedReference();
        fixture.Store.Set(fixture.Reference.AssetId, fixture.Reference.Version, Png.Concat(new byte[] { 9 }).ToArray());
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("HTTP must not be called")),
            true,
            registry: fixture.Registry,
            store: fixture.Store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [fixture.Reference]),
            TestContext.Current.CancellationToken));

        Assert.Equal("identity_asset_bytes_invalid", exception.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    [InlineData(HttpStatusCode.OK, "{\"name\":\"../escape.png\",\"subfolder\":\"\",\"type\":\"input\"}")]
    public async Task GenerateAsync_MapsReferenceUploadFailures(
        HttpStatusCode uploadStatus,
        string uploadBody)
    {
        var fixture = ApprovedReference();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal("/upload/image", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(uploadStatus, uploadBody));
        });
        var provider = CreateProvider(handler, true, registry: fixture.Registry, store: fixture.Store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [fixture.Reference]),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_reference_upload_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsWhenDisabled()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("should not be called")),
            enabled: false);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_disabled", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsMissingWorkflow()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("should not be called")),
            enabled: true,
            workflow: Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_workflow_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsUnreachableServerToUnavailable()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new HttpRequestException("connection refused")),
            enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_provider_unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsCompletedWithoutImageToFailure()
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

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsPollTimeout()
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

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_timeout", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_HoldsGateUntilGenerationCompletes()
    {
        var gate = new RecordingGpuResourceGate();
        var generationStarted = new TaskCompletionSource();
        var releaseGeneration = new TaskCompletionSource();

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/prompt")
            {
                return Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}");
            }

            if (path.StartsWith("/history/", StringComparison.Ordinal))
            {
                generationStarted.TrySetResult();
                await releaseGeneration.Task.WaitAsync(TestContext.Current.CancellationToken);
                return Json(HttpStatusCode.OK, History);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Png)
            };
        });
        var provider = CreateProvider(handler, enabled: true, gpuResourceGate: gate);

        var run = provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken);

        await generationStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, gate.ActiveLeases);
        Assert.Equal(1, gate.AcquireCount);
        Assert.Contains("acquire:comfyui", gate.Events);

        releaseGeneration.TrySetResult();
        await run;

        Assert.Equal(0, gate.ActiveLeases);
        Assert.Contains("release:comfyui", gate.Events);
    }

    [Fact]
    public void IsEnabled_ReflectsConfiguration()
    {
        Assert.True(CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException()),
            enabled: true).IsEnabled);
        Assert.False(CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException()),
            enabled: false).IsEnabled);
    }

    private ComfyUiImageGenerationProvider CreateProvider(
        HttpMessageHandler handler,
        bool enabled,
        string? workflow = null,
        string? editWorkflow = null,
        int timeoutSeconds = 600,
        IGpuResourceGate? gpuResourceGate = null,
        IIdentityAssetRegistry? registry = null,
        IIdentityAssetStore? store = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8188/") },
            Options.Create(new ComfyUiOptions
            {
                Enabled = enabled,
                BaseUrl = "http://127.0.0.1:8188",
                WorkflowPath = workflow ?? workflowPath,
                ImageEditWorkflowPath = editWorkflow ?? editWorkflowPath,
                TimeoutSeconds = timeoutSeconds,
                Width = 1280,
                Height = 720
            }),
            gpuResourceGate ?? NoopGpuResourceGate.Instance,
            registry ?? new IdentityAssetRegistry(),
            store ?? new FakeIdentityAssetStore());

    private static StubHttpMessageHandler SuccessHandler(
        Func<HttpRequestMessage, CancellationToken, Task>? inspect = null) =>
        new(async (request, cancellationToken) =>
        {
            if (inspect is not null)
            {
                await inspect(request, cancellationToken);
            }

            var path = request.RequestUri!.PathAndQuery;
            if (request.RequestUri.AbsolutePath == "/upload/image")
            {
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                var filename = multipart.Single(part =>
                        part.Headers.ContentDisposition!.Name!.Trim('"') == "image")
                    .Headers.ContentDisposition!.FileName!.Trim('"');
                return Json(
                    HttpStatusCode.OK,
                    $"{{\"name\":\"{filename}\",\"subfolder\":\"\",\"type\":\"input\"}}");
            }

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
                    Content = new ByteArrayContent(Png)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

    private static ReferenceFixture ApprovedReference(
        int version = 1,
        string kind = "reference-image",
        string mediaType = "image/png",
        bool approve = true,
        bool includeBytes = true)
    {
        var asset = Asset(version, Png, kind, mediaType);
        var registry = new IdentityAssetRegistry([asset]);
        if (approve)
        {
            registry.Approve(asset.Id, asset.Version);
        }

        var store = new FakeIdentityAssetStore();
        if (includeBytes)
        {
            store.Set(asset.Id, asset.Version, Png);
        }

        return new ReferenceFixture(
            registry,
            store,
            new PinnedIdentityAsset(asset.Id, asset.Version));
    }

    private static IdentityAsset Asset(
        int version,
        byte[] bytes,
        string kind = "reference-image",
        string mediaType = "image/png") =>
        IdentityAssetTestSupport.Asset(
            id: "student-reference",
            version: version,
            kind: kind,
            mediaType: mediaType) with
        {
            ByteSize = bytes.Length,
            ContentHash = Hash(bytes)
        };

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private const string Template = """
        {
          "1": { "class_type": "CLIPTextEncode", "inputs": { "text": "__PROMPT__" } },
          "2": { "class_type": "Flux2Scheduler", "inputs": { "width": "__WIDTH__", "height": "__HEIGHT__" } },
          "3": { "class_type": "RandomNoise", "inputs": { "noise_seed": "__SEED__" } },
          "4": { "class_type": "UNETLoader", "inputs": { "unet_name": "trusted-text-model.safetensors" } }
        }
        """;

    private const string EditTemplate = """
        {
          "1": { "class_type": "CLIPTextEncode", "inputs": { "text": "__PROMPT__" } },
          "2": { "class_type": "RandomNoise", "inputs": { "noise_seed": "__SEED__" } },
          "3": { "class_type": "LoadImage", "inputs": { "image": "__REFERENCE_IMAGE__" } },
          "4": { "class_type": "UNETLoader", "inputs": { "unet_name": "trusted-edit-model.safetensors" } },
          "5": { "class_type": "EmptyFlux2LatentImage", "inputs": { "width": "__WIDTH__", "height": "__HEIGHT__" } }
        }
        """;

    private const string History = """
        {
          "abc": {
            "status": { "completed": true, "status_str": "success" },
            "outputs": {
              "9": {
                "images": [
                  { "filename": "abc_00001_.png", "subfolder": "aistudio", "type": "output" }
                ]
              }
            }
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
