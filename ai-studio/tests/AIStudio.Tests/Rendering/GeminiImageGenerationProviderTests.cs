using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Rendering.Gemini;
using AIStudio.Tests.IdentityAssets;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GeminiImageGenerationProviderTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private const string DefaultApiKey = "secret-gemini-key";

    [Theory]
    [InlineData(true, "test-api-key", true)]
    [InlineData(false, "test-api-key", false)]
    [InlineData(true, "", false)]
    [InlineData(true, "   ", false)]
    [InlineData(false, "", false)]
    public void IsEnabled_ReflectsConfiguration(bool enabled, string apiKey, bool expected)
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException()),
            enabled: enabled,
            apiKey: apiKey);

        Assert.Equal(expected, provider.IsEnabled);
    }

    [Theory]
    [InlineData(false, "test-api-key")]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task GenerateAsync_DisabledOrMissingApiKey_ThrowsDisabled(bool enabled, string apiKey)
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: enabled,
            apiKey: apiKey);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("a beautiful sunset", 42),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_disabled", exception.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateAsync_EmptyPrompt_ThrowsPromptEmpty(string prompt)
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: DefaultApiKey);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest(prompt, 42),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_prompt_empty", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_ZeroPinTextToImage_SendsCorrectPayloadAndHeaders_AndReturnsPng()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(HttpStatusCode.OK, BuildGeminiResponse(Png));
        });

        var provider = CreateProvider(handler, enabled: true, apiKey: DefaultApiKey);

        var result = await provider.GenerateAsync(
            new ImageGenerationRequest("  a high-tech laboratory  ", 12345),
            TestContext.Current.CancellationToken);

        Assert.Equal(Png, result);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);

        // Header check: x-goog-api-key must be present with the exact key
        Assert.True(capturedRequest.Headers.Contains("x-goog-api-key"));
        Assert.Equal(DefaultApiKey, capturedRequest.Headers.GetValues("x-goog-api-key").Single());

        // URL check: no API key in path or query
        Assert.DoesNotContain(DefaultApiKey, capturedRequest.RequestUri!.ToString());
        Assert.DoesNotContain("key=", capturedRequest.RequestUri.Query);
        Assert.Contains("v1beta/models/gemini-2.5-flash-image:generateContent", capturedRequest.RequestUri.AbsolutePath);

        // Body JSON check
        Assert.NotNull(capturedBody);
        var json = JsonNode.Parse(capturedBody)!.AsObject();

        var contents = json["contents"]!.AsArray();
        Assert.Single(contents);
        var parts = contents[0]!["parts"]!.AsArray();
        Assert.Single(parts);
        Assert.Equal("a high-tech laboratory", parts[0]!["text"]!.GetValue<string>());
        Assert.Null(parts[0]!["inlineData"]);

        var genConfig = json["generationConfig"]!.AsObject();
        var modalities = genConfig["responseModalities"]!.AsArray();
        Assert.Equal("IMAGE", modalities[0]!.GetValue<string>());
        Assert.Equal("16:9", genConfig["imageConfig"]!["aspectRatio"]!.GetValue<string>());
    }

    [Fact]
    public async Task GenerateAsync_SingleApprovedReference_ValidatesAndSendsInlineData_AndReturnsPng()
    {
        var fixture = ApprovedReference();
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(HttpStatusCode.OK, BuildGeminiResponse(Png));
        });

        var provider = CreateProvider(
            handler,
            enabled: true,
            apiKey: DefaultApiKey,
            registry: fixture.Registry,
            store: fixture.Store);

        const string prompt = "generate character in sci-fi suit";
        var result = await provider.GenerateAsync(
            new ImageGenerationRequest(prompt, 999, [fixture.Reference]),
            TestContext.Current.CancellationToken);

        Assert.Equal(Png, result);
        Assert.Equal([(fixture.Reference.AssetId, fixture.Reference.Version)], fixture.Store.Reads);

        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedBody);

        var json = JsonNode.Parse(capturedBody)!.AsObject();
        var parts = json["contents"]![0]!["parts"]!.AsArray();
        Assert.Equal(2, parts.Count);

        var inlineData = parts[0]!["inlineData"]!.AsObject();
        Assert.Equal("image/png", inlineData["mimeType"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(Png), inlineData["data"]!.GetValue<string>());

        Assert.Equal(prompt, parts[1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task GenerateAsync_ReadsRequestedVersionAndNeverLatestApproved()
    {
        var firstBytes = Png;
        var secondBytes = Png.Concat(new byte[] { 5, 6, 7, 8 }).ToArray();
        var first = Asset(version: 1, bytes: firstBytes);
        var second = Asset(version: 2, bytes: secondBytes);

        var registry = new IdentityAssetRegistry([first, second]);
        registry.Approve(first.Id, first.Version);
        registry.Approve(second.Id, second.Version);

        var store = new FakeIdentityAssetStore();
        store.Set(first.Id, first.Version, firstBytes);
        store.Set(second.Id, second.Version, secondBytes);

        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(HttpStatusCode.OK, BuildGeminiResponse(Png));
        });

        var provider = CreateProvider(
            handler,
            enabled: true,
            apiKey: DefaultApiKey,
            registry: registry,
            store: store);

        await provider.GenerateAsync(
            new ImageGenerationRequest(
                "prompt",
                1,
                [new PinnedIdentityAsset(first.Id, first.Version)]),
            TestContext.Current.CancellationToken);

        Assert.NotNull(capturedBody);
        var json = JsonNode.Parse(capturedBody)!.AsObject();
        var sentBase64 = json["contents"]![0]!["parts"]![0]!["inlineData"]!["data"]!.GetValue<string>();
        Assert.Equal(Convert.ToBase64String(firstBytes), sentBase64);

        Assert.Equal([(first.Id, first.Version)], store.Reads);
        Assert.DoesNotContain((second.Id, second.Version), store.Reads);
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
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: DefaultApiKey,
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
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: DefaultApiKey,
            registry: fixture.Registry,
            store: fixture.Store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [missingPin]),
            TestContext.Current.CancellationToken));

        Assert.Equal("identity_asset_not_found", exception.ErrorCode);
        Assert.Empty(fixture.Store.Reads);
    }

    [Fact]
    public async Task GenerateAsync_RejectsMissingStoreBytes()
    {
        var fixture = ApprovedReference(includeBytes: false);
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: DefaultApiKey,
            registry: fixture.Registry,
            store: fixture.Store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [fixture.Reference]),
            TestContext.Current.CancellationToken));

        Assert.Equal("identity_asset_bytes_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_RejectsHashMismatchedBytes()
    {
        var fixture = ApprovedReference();
        // Replace store bytes with corrupted PNG data that does not match the metadata hash
        var corrupted = Png.Concat(new byte[] { 99, 98, 97 }).ToArray();
        fixture.Store.Set(fixture.Reference.AssetId, fixture.Reference.Version, corrupted);

        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: DefaultApiKey,
            registry: fixture.Registry,
            store: fixture.Store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [fixture.Reference]),
            TestContext.Current.CancellationToken));

        Assert.Equal("identity_asset_bytes_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_RejectsNonPngStoredBytes()
    {
        byte[] nonPngBytes = [1, 2, 3, 4, 5, 6, 7, 8];
        var asset = Asset(1, nonPngBytes);
        var registry = new IdentityAssetRegistry([asset]);
        registry.Approve(asset.Id, asset.Version);
        var store = new FakeIdentityAssetStore();
        store.Set(asset.Id, asset.Version, nonPngBytes);

        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: DefaultApiKey,
            registry: registry,
            store: store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [new PinnedIdentityAsset(asset.Id, asset.Version)]),
            TestContext.Current.CancellationToken));

        Assert.Equal("identity_asset_bytes_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_RejectsReferenceExceedingMaxByteSize()
    {
        // Metadata says 9 MB, byte array is 9 MB
        var bigBytes = new byte[9 * 1024 * 1024];
        // PNG header at beginning
        Png.CopyTo(bigBytes, 0);

        var asset = Asset(1, bigBytes);
        var registry = new IdentityAssetRegistry([asset]);
        registry.Approve(asset.Id, asset.Version);
        var store = new FakeIdentityAssetStore();
        store.Set(asset.Id, asset.Version, bigBytes);

        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: DefaultApiKey,
            registry: registry,
            store: store);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1, [new PinnedIdentityAsset(asset.Id, asset.Version)]),
            TestContext.Current.CancellationToken));

        Assert.Equal("identity_asset_bytes_invalid", exception.ErrorCode);
    }

    [Theory]
    [InlineData("not-base64!!!")]
    [InlineData("aGVsbG8gd29ybGQ=")] // valid base64 but "hello world", not PNG
    [InlineData("")]
    public async Task GenerateAsync_NonPngInlineResponseData_ThrowsOutputInvalid(string base64Payload)
    {
        var responseJson = $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "inlineData": {
                      "mimeType": "image/png",
                      "data": "{{base64Payload}}"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, responseJson)));

        var provider = CreateProvider(handler, enabled: true, apiKey: DefaultApiKey);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_output_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MissingInlineDataInResponse_ThrowsOutputInvalid()
    {
        const string responseJson = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "I am sorry, I cannot generate this image."
                  }
                ]
              }
            }
          ]
        }
        """;

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, responseJson)));

        var provider = CreateProvider(handler, enabled: true, apiKey: DefaultApiKey);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_output_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_SnakeCaseInlineData_ParsesSuccessfully()
    {
        var snakeCaseJson = $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "inline_data": {
                      "mime_type": "image/png",
                      "data": "{{Convert.ToBase64String(Png)}}"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, snakeCaseJson)));

        var provider = CreateProvider(handler, enabled: true, apiKey: DefaultApiKey);

        var result = await provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken);

        Assert.Equal(Png, result);
    }

    [Fact]
    public async Task GenerateAsync_Timeout_MapsToImageGenerationTimeout()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return JsonResponse(HttpStatusCode.OK, BuildGeminiResponse(Png));
        });

        var provider = CreateProvider(
            handler,
            enabled: true,
            apiKey: DefaultApiKey,
            timeoutSeconds: 1);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_timeout", exception.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "internal server error")]
    [InlineData(HttpStatusCode.BadRequest, "invalid argument")]
    public async Task GenerateAsync_HttpErrorResponse_MapsToImageGenerationFailed(
        HttpStatusCode status,
        string errorBody)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(status, errorBody)));

        var provider = CreateProvider(handler, enabled: true, apiKey: DefaultApiKey);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_HttpTransportException_MapsToImageGenerationFailed()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("connection dropped"));

        var provider = CreateProvider(handler, enabled: true, apiKey: DefaultApiKey);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MalformedJson_MapsToImageGenerationFailed()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "not-valid-json{")));

        var provider = CreateProvider(handler, enabled: true, apiKey: DefaultApiKey);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(() => provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_failed", exception.ErrorCode);
    }

    [Fact]
    public void Constructor_DoesNotRequireGpuResourceGate()
    {
        var constructors = typeof(GeminiImageGenerationProvider).GetConstructors();
        Assert.Single(constructors);

        var parameters = constructors[0].GetParameters();
        Assert.Equal(4, parameters.Length);

        Assert.Equal(typeof(HttpClient), parameters[0].ParameterType);
        Assert.Equal(typeof(IOptions<GeminiImageOptions>), parameters[1].ParameterType);
        Assert.Equal(typeof(IIdentityAssetRegistry), parameters[2].ParameterType);
        Assert.Equal(typeof(IIdentityAssetStore), parameters[3].ParameterType);

        Assert.DoesNotContain(parameters, p => p.ParameterType.Name.Contains("Gpu", StringComparison.OrdinalIgnoreCase));
    }

    private static GeminiImageGenerationProvider CreateProvider(
        HttpMessageHandler handler,
        bool enabled,
        string apiKey = DefaultApiKey,
        string model = "gemini-2.5-flash-image",
        string aspectRatio = "16:9",
        int timeoutSeconds = 120,
        IIdentityAssetRegistry? registry = null,
        IIdentityAssetStore? store = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") },
            Options.Create(new GeminiImageOptions
            {
                Enabled = enabled,
                BaseUrl = "https://generativelanguage.googleapis.com/",
                ApiKey = apiKey,
                Model = model,
                AspectRatio = aspectRatio,
                TimeoutSeconds = timeoutSeconds
            }),
            registry ?? new IdentityAssetRegistry(),
            store ?? new FakeIdentityAssetStore());

    private static string BuildGeminiResponse(byte[] pngBytes) =>
        $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "inlineData": {
                      "mimeType": "image/png",
                      "data": "{{Convert.ToBase64String(pngBytes)}}"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

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
