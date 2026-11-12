using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIStudio.Application.Assets;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering.Gemini;

/// <summary>
/// Cloud-hosted Gemini Nano Banana image generation engine. Zero-reference requests
/// send the text prompt directly to Gemini. A single concrete approved identity reference
/// is read from the store, validated for exact approval and PNG integrity, and included
/// as inline image data in the request. Does not consume or lock local GPU semaphore leases.
/// </summary>
public sealed class GeminiImageGenerationProvider : IImageGenerationProvider
{
    private const string SupportedReferenceKind = "reference-image";
    private const string SupportedReferenceMediaType = "image/png";
    private const long MaxReferenceByteSize = 8 * 1024 * 1024;

    private readonly HttpClient httpClient;
    private readonly GeminiImageOptions options;
    private readonly IIdentityAssetRegistry identityAssetRegistry;
    private readonly IIdentityAssetStore identityAssetStore;

    public GeminiImageGenerationProvider(
        HttpClient httpClient,
        IOptions<GeminiImageOptions> options,
        IIdentityAssetRegistry identityAssetRegistry,
        IIdentityAssetStore identityAssetStore)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options.Value ?? throw new ArgumentNullException(nameof(options));
        this.identityAssetRegistry = identityAssetRegistry ?? throw new ArgumentNullException(nameof(identityAssetRegistry));
        this.identityAssetStore = identityAssetStore ?? throw new ArgumentNullException(nameof(identityAssetStore));
    }

    public bool IsEnabled => options.Enabled && !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<byte[]> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new RenderVideoException(
                "image_generation_disabled",
                "The Gemini Nano Banana image engine is not enabled.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new RenderVideoException(
                "image_prompt_empty",
                "Image generation requires a non-empty prompt.");
        }

        if (request.IdentityReferences.Count > 1)
        {
            throw new RenderVideoException(
                "identity_reference_count_unsupported",
                "Gemini identity-reference consumption supports at most one reference in v1.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.TimeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        }

        try
        {
            byte[]? referenceBytes = null;
            if (request.IdentityReferences.Count == 1)
            {
                referenceBytes = await PrepareReferenceAsync(
                    request.IdentityReferences[0],
                    timeout.Token);
            }

            var parts = new JsonArray();
            if (referenceBytes is not null)
            {
                parts.Add(new JsonObject
                {
                    ["inlineData"] = new JsonObject
                    {
                        ["mimeType"] = "image/png",
                        ["data"] = Convert.ToBase64String(referenceBytes)
                    }
                });
            }

            parts.Add(new JsonObject
            {
                ["text"] = request.Prompt.Trim()
            });

            var generationConfig = new JsonObject
            {
                ["responseModalities"] = new JsonArray { "IMAGE" }
            };

            if (!string.IsNullOrWhiteSpace(options.AspectRatio))
            {
                generationConfig["imageConfig"] = new JsonObject
                {
                    ["aspectRatio"] = options.AspectRatio
                };
            }

            var payload = new JsonObject
            {
                ["contents"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["parts"] = parts
                    }
                },
                ["generationConfig"] = generationConfig
            };

            var relativeUri = $"v1beta/models/{Uri.EscapeDataString(options.Model)}:generateContent";
            var requestUri = httpClient.BaseAddress is not null
                ? new Uri(relativeUri, UriKind.Relative)
                : new Uri(new Uri(EnsureTrailingSlash(options.BaseUrl)), relativeUri);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
            httpRequest.Headers.Add("x-goog-api-key", options.ApiKey);
            httpRequest.Content = new StringContent(
                payload.ToJsonString(),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(httpRequest, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(timeout.Token);
                throw new RenderVideoException(
                    "image_generation_failed",
                    $"Gemini image generation rejected the request ({(int)response.StatusCode}): {Summarize(error)}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);
            return ExtractAndValidateImage(responseBody);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new RenderVideoException(
                "image_generation_timeout",
                $"Gemini image generation exceeded {options.TimeoutSeconds} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new RenderVideoException(
                "image_generation_failed",
                $"Gemini image generation HTTP request failed: {exception.Message}",
                exception);
        }
    }

    private async Task<byte[]> PrepareReferenceAsync(
        PinnedIdentityAsset reference,
        CancellationToken cancellationToken)
    {
        if (!identityAssetRegistry.TryGet(reference.AssetId, reference.Version, out var metadata))
        {
            throw new RenderVideoException(
                "identity_asset_not_found",
                $"Identity asset '{reference.AssetId}' v{reference.Version.Value} was not found.");
        }

        if (metadata.Id != reference.AssetId || metadata.Version != reference.Version)
        {
            throw new RenderVideoException(
                "identity_asset_not_found",
                $"Identity asset '{reference.AssetId}' v{reference.Version.Value} did not resolve exactly.");
        }

        if (metadata.Status != IdentityAssetStatus.Approved)
        {
            throw new RenderVideoException(
                "identity_asset_not_approved",
                $"Identity asset '{reference.AssetId}' v{reference.Version.Value} is not approved.");
        }

        if (!string.Equals(metadata.Kind, SupportedReferenceKind, StringComparison.Ordinal))
        {
            throw new RenderVideoException(
                "identity_asset_unsupported_kind",
                $"Identity asset kind '{metadata.Kind}' cannot be consumed as an image reference.");
        }

        if (!string.Equals(metadata.MediaType, SupportedReferenceMediaType, StringComparison.Ordinal))
        {
            throw new RenderVideoException(
                "identity_asset_unsupported_media_type",
                $"Identity asset media type '{metadata.MediaType}' is not supported for references.");
        }

        if (!identityAssetStore.Exists(reference.AssetId, reference.Version))
        {
            throw MissingBytes(reference);
        }

        ReadOnlyMemory<byte> bytes;
        try
        {
            bytes = await identityAssetStore.ReadBytesAsync(
                reference.AssetId,
                reference.Version,
                cancellationToken);
        }
        catch (AssetCollectionException exception)
            when (exception.ErrorCode == "asset_file_not_found")
        {
            throw MissingBytes(reference, exception);
        }
        catch (FileNotFoundException exception)
        {
            throw MissingBytes(reference, exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw MissingBytes(reference, exception);
        }

        var actualHash = Convert
            .ToHexString(SHA256.HashData(bytes.Span))
            .ToLowerInvariant();
        if (bytes.Length == 0
            || (long)bytes.Length > MaxReferenceByteSize
            || (long)bytes.Length != metadata.ByteSize
            || !string.Equals(actualHash, metadata.ContentHash, StringComparison.Ordinal)
            || !IsPng(bytes.Span))
        {
            throw new RenderVideoException(
                "identity_asset_bytes_invalid",
                $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} do not match approved metadata.");
        }

        return bytes.ToArray();
    }

    private static byte[] ExtractAndValidateImage(string responseJson)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(responseJson);
        }
        catch (JsonException exception)
        {
            throw new RenderVideoException(
                "image_generation_failed",
                $"Gemini returned an unexpected response: {exception.Message}",
                exception);
        }

        if (root is not JsonObject rootObj)
        {
            throw new RenderVideoException(
                "image_output_invalid",
                "Gemini response root is not a JSON object.");
        }

        var base64Data = FindInlineData(rootObj);
        if (string.IsNullOrWhiteSpace(base64Data))
        {
            throw new RenderVideoException(
                "image_output_invalid",
                "Gemini response did not contain inline image data.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException exception)
        {
            throw new RenderVideoException(
                "image_output_invalid",
                "Gemini response inline image data is not valid base64.",
                exception);
        }

        if (bytes.Length == 0 || !IsPng(bytes))
        {
            throw new RenderVideoException(
                "image_output_invalid",
                "Gemini response did not contain a valid PNG image.");
        }

        return bytes;
    }

    private static string? FindInlineData(JsonObject root)
    {
        if (root["candidates"] is not JsonArray candidates)
        {
            return null;
        }

        foreach (var candidateNode in candidates)
        {
            if (candidateNode is not JsonObject candidate)
            {
                continue;
            }

            if (candidate["content"] is not JsonObject content)
            {
                continue;
            }

            if (content["parts"] is not JsonArray parts)
            {
                continue;
            }

            foreach (var partNode in parts)
            {
                if (partNode is not JsonObject part)
                {
                    continue;
                }

                JsonObject? inlineData = null;
                if (part["inlineData"] is JsonObject camel)
                {
                    inlineData = camel;
                }
                else if (part["inline_data"] is JsonObject snake)
                {
                    inlineData = snake;
                }

                if (inlineData is not null)
                {
                    var data = inlineData["data"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(data))
                    {
                        return data;
                    }
                }
            }
        }

        return null;
    }

    private static RenderVideoException MissingBytes(
        PinnedIdentityAsset reference,
        Exception? innerException = null) =>
        new(
            "identity_asset_bytes_missing",
            $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} were not found.",
            innerException);

    private static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8
        && bytes[0] == 0x89
        && bytes[1] == 0x50
        && bytes[2] == 0x4E
        && bytes[3] == 0x47
        && bytes[4] == 0x0D
        && bytes[5] == 0x0A
        && bytes[6] == 0x1A
        && bytes[7] == 0x0A;

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";

    private static string Summarize(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}
