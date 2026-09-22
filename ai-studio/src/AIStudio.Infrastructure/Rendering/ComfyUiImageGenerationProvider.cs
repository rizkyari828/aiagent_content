using System.Globalization;
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

namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Local ComfyUI image engine. Zero-reference requests preserve the trusted
/// text-to-image workflow. A single concrete approved identity reference is read
/// through the application store, uploaded to ComfyUI, and injected only into the
/// repository-owned Flux.2 Klein image-edit workflow.
/// </summary>
public sealed class ComfyUiImageGenerationProvider : IImageGenerationProvider
{
    private const string SupportedReferenceKind = "reference-image";
    private const string SupportedReferenceMediaType = "image/png";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly HttpClient httpClient;
    private readonly ComfyUiOptions options;
    private readonly IGpuResourceGate gpuResourceGate;
    private readonly IIdentityAssetRegistry identityAssetRegistry;
    private readonly IIdentityAssetStore identityAssetStore;

    public ComfyUiImageGenerationProvider(
        HttpClient httpClient,
        IOptions<ComfyUiOptions> options,
        IGpuResourceGate gpuResourceGate,
        IIdentityAssetRegistry identityAssetRegistry,
        IIdentityAssetStore identityAssetStore)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.gpuResourceGate = gpuResourceGate;
        this.identityAssetRegistry = identityAssetRegistry;
        this.identityAssetStore = identityAssetStore;
    }

    public bool IsEnabled => options.Enabled;

    public async Task<byte[]> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled)
        {
            throw new RenderVideoException(
                "image_generation_disabled",
                "The ComfyUI image engine is not enabled.");
        }

        if (request.IdentityReferences.Count > 1)
        {
            throw new RenderVideoException(
                "identity_reference_count_unsupported",
                "ComfyUI identity-reference consumption supports at most one reference in v1.");
        }

        var configuredWorkflowPath = request.IdentityReferences.Count == 0
            ? options.WorkflowPath
            : options.ImageEditWorkflowPath;
        var workflowPath = ResolveWorkflowPath(configuredWorkflowPath);
        if (!File.Exists(workflowPath))
        {
            throw new RenderVideoException(
                "image_workflow_not_found",
                $"ComfyUI workflow '{configuredWorkflowPath}' was not found.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            string? uploadedReference = null;
            if (request.IdentityReferences.Count == 1)
            {
                uploadedReference = await PrepareReferenceAsync(
                    request.IdentityReferences[0],
                    timeout.Token);
            }

            var graph = BuildGraph(
                await File.ReadAllTextAsync(workflowPath, timeout.Token),
                request,
                uploadedReference);
            var clientId = Guid.NewGuid().ToString("N");

            // Upload does not use the GPU. Hold the exclusive lease only for the
            // complete submit -> poll -> download window, never just POST /prompt.
            await using var lease = await gpuResourceGate.AcquireAsync(
                GpuWorkloads.ComfyUi,
                cancellationToken);

            var promptId = await SubmitAsync(graph, clientId, timeout.Token);
            var image = await WaitForImageAsync(promptId, timeout.Token);
            return await DownloadAsync(image, timeout.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new RenderVideoException(
                "image_generation_timeout",
                $"ComfyUI exceeded {options.TimeoutSeconds} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new RenderVideoException(
                "image_provider_unavailable",
                $"Unable to reach ComfyUI at '{options.BaseUrl}': {exception.Message}",
                exception);
        }
        catch (JsonException exception)
        {
            throw new RenderVideoException(
                "image_generation_failed",
                $"ComfyUI returned an unexpected response: {exception.Message}",
                exception);
        }
    }

    private async Task<string> PrepareReferenceAsync(
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
        if (bytes.Length != metadata.ByteSize
            || !string.Equals(actualHash, metadata.ContentHash, StringComparison.Ordinal)
            || !IsPng(bytes.Span))
        {
            throw new RenderVideoException(
                "identity_asset_bytes_invalid",
                $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} do not match approved metadata.");
        }

        var filename = BuildUploadFilename(reference, metadata.ContentHash);
        return await UploadReferenceAsync(
            bytes,
            metadata.MediaType,
            filename,
            cancellationToken);
    }

    private async Task<string> UploadReferenceAsync(
        ReadOnlyMemory<byte> bytes,
        string mediaType,
        string filename,
        CancellationToken cancellationToken)
    {
        using var imageContent = new ByteArrayContent(bytes.ToArray());
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(imageContent, "image", filename);
        multipart.Add(new StringContent("input", Encoding.UTF8), "type");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync("upload/image", multipart, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new RenderVideoException(
                "image_reference_upload_failed",
                $"ComfyUI reference upload failed: {exception.Message}",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new RenderVideoException(
                    "image_reference_upload_failed",
                    $"ComfyUI rejected the reference upload ({(int)response.StatusCode}): {Summarize(error)}");
            }

            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var upload = JsonNode.Parse(body) as JsonObject;
                var returnedName = upload?["name"]?.GetValue<string>();
                var returnedSubfolder = upload?["subfolder"]?.GetValue<string>() ?? string.Empty;
                var returnedType = upload?["type"]?.GetValue<string>();

                if (!string.Equals(returnedName, filename, StringComparison.Ordinal)
                    || !string.IsNullOrEmpty(returnedSubfolder)
                    || !string.Equals(returnedType, "input", StringComparison.Ordinal))
                {
                    throw new RenderVideoException(
                        "image_reference_upload_failed",
                        "ComfyUI returned an unexpected reference upload location.");
                }

                return returnedName!;
            }
            catch (JsonException exception)
            {
                throw new RenderVideoException(
                    "image_reference_upload_failed",
                    "ComfyUI returned a malformed reference upload response.",
                    exception);
            }
            catch (InvalidOperationException exception)
            {
                throw new RenderVideoException(
                    "image_reference_upload_failed",
                    "ComfyUI returned a malformed reference upload response.",
                    exception);
            }
        }
    }

    /// <summary>
    /// Injects bounded values into one fixed repository-owned workflow. Values are
    /// JSON-encoded, so neither prompt nor uploaded filename can alter graph shape.
    /// </summary>
    private JsonNode BuildGraph(
        string template,
        ImageGenerationRequest request,
        string? uploadedReference)
    {
        var encodedPrompt = JsonString(request.Prompt);
        var text = template
            .Replace("\"__PROMPT__\"", encodedPrompt, StringComparison.Ordinal)
            .Replace("\"__WIDTH__\"", options.Width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("\"__HEIGHT__\"", options.Height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("\"__SEED__\"", NormalizeSeed(request.Seed), StringComparison.Ordinal);

        if (uploadedReference is not null)
        {
            text = text.Replace(
                "\"__REFERENCE_IMAGE__\"",
                JsonString(uploadedReference),
                StringComparison.Ordinal);
        }

        return JsonNode.Parse(text)
            ?? throw new RenderVideoException(
                "image_workflow_invalid",
                "The ComfyUI workflow template is not valid JSON.");
    }

    private async Task<string> SubmitAsync(
        JsonNode graph,
        string clientId,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["prompt"] = graph,
            ["client_id"] = clientId
        }.ToJsonString();

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync("prompt", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new RenderVideoException(
                "image_generation_failed",
                $"ComfyUI rejected the workflow ({(int)response.StatusCode}): {Summarize(error)}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var promptId = (JsonNode.Parse(responseBody) as JsonObject)?["prompt_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(promptId))
        {
            throw new RenderVideoException(
                "image_generation_failed",
                "ComfyUI did not return a prompt id.");
        }

        return promptId;
    }

    private async Task<ComfyImage> WaitForImageAsync(
        string promptId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var response = await httpClient.GetAsync(
                $"history/{Uri.EscapeDataString(promptId)}",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (JsonNode.Parse(body) is JsonObject history
                && history.TryGetPropertyValue(promptId, out var entry)
                && entry is JsonObject job
                && job["status"] is JsonObject status
                && status["completed"]?.GetValue<bool>() == true)
            {
                var statusText = status["status_str"]?.GetValue<string>();
                if (!string.Equals(statusText, "success", StringComparison.Ordinal))
                {
                    throw new RenderVideoException(
                        "image_generation_failed",
                        $"ComfyUI reported status '{statusText}'.");
                }

                return FindImage(job)
                    ?? throw new RenderVideoException(
                        "image_generation_failed",
                        "ComfyUI completed without producing an image.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static ComfyImage? FindImage(JsonObject job)
    {
        if (job["outputs"] is not JsonObject outputs)
        {
            return null;
        }

        foreach (var (_, output) in outputs)
        {
            if (output?["images"] is not JsonArray images)
            {
                continue;
            }

            foreach (var imageNode in images)
            {
                if (imageNode is not JsonObject image)
                {
                    continue;
                }

                var filename = image["filename"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(filename))
                {
                    continue;
                }

                return new ComfyImage(
                    filename,
                    image["subfolder"]?.GetValue<string>() ?? string.Empty,
                    image["type"]?.GetValue<string>() ?? "output");
            }
        }

        return null;
    }

    private async Task<byte[]> DownloadAsync(
        ComfyImage image,
        CancellationToken cancellationToken)
    {
        var query =
            $"view?filename={Uri.EscapeDataString(image.Filename)}"
            + $"&subfolder={Uri.EscapeDataString(image.Subfolder)}"
            + $"&type={Uri.EscapeDataString(image.Type)}";

        using var response = await httpClient.GetAsync(query, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new RenderVideoException(
                "image_download_failed",
                $"ComfyUI image download failed ({(int)response.StatusCode}).");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!IsPng(bytes))
        {
            throw new RenderVideoException(
                "image_download_failed",
                "ComfyUI did not return a valid PNG image.");
        }

        return bytes;
    }

    private string ResolveWorkflowPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, configuredPath),
            Path.Combine(Directory.GetCurrentDirectory(), configuredPath)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static RenderVideoException MissingBytes(
        PinnedIdentityAsset reference,
        Exception? innerException = null) =>
        new(
            "identity_asset_bytes_missing",
            $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} were not found.",
            innerException);

    private static string BuildUploadFilename(
        PinnedIdentityAsset reference,
        string contentHash) =>
        $"aistudio-{reference.AssetId.Value}-v{reference.Version.Value}-{contentHash[..8]}.png";

    private static string JsonString(string value) =>
        "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    private static string NormalizeSeed(long seed) =>
        ((ulong)seed).ToString(CultureInfo.InvariantCulture);

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

    private static string Summarize(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private sealed record ComfyImage(string Filename, string Subfolder, string Type);
}
