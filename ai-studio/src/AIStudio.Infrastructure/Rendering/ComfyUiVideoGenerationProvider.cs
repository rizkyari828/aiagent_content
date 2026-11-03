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
/// Local ComfyUI image-to-video adapter (scaffold). It implements the verified
/// ComfyUI HTTP shape already used by the image engine: one trusted repository-owned
/// workflow is submitted to <c>/prompt</c>, polled through <c>/history</c>, and the
/// produced clip is downloaded through <c>/view</c>. Only prompt, duration, fps,
/// dimensions, and seed are injected, so a caller can never submit a graph, model,
/// node, path, or URL.
///
/// The exclusive GPU lease is held across the complete submit -> poll -> download
/// window and released before the CPU-only persist and <c>IMediaInspector</c> probe.
/// The probe is the artifact authority: a clip is accepted only when it has a video
/// stream, a plausible duration, and exactly the requested dimensions.
///
/// TODO(verify-on-home): the repository-owned I2V workflow file does not exist yet.
/// The graph, its output node, and the produced container must be verified on the GPU
/// machine before <see cref="ComfyUiVideoOptions.WorkflowPath"/> points at a real file.
/// Until then a missing file reports <c>video_not_configured</c>; no node or model name
/// is invented here.
/// </summary>
public sealed class ComfyUiVideoGenerationProvider : IVideoGenerationProvider
{
    private const string SupportedReferenceKind = "reference-image";
    private const string SupportedReferenceMediaType = "image/png";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Duration tolerance: <c>max(0.25 s, 5% of the requested duration)</c>. Encoders
    /// commonly emit frame-aligned durations that differ slightly from the requested
    /// floating-point length, so an exact duration match would be brittle.
    /// </summary>
    private const double MinimumDurationToleranceSeconds = 0.25;
    private const double DurationToleranceRatio = 0.05;

    private static readonly string[] RequiredPlaceholders =
    [
        "\"__PROMPT__\"",
        "\"__WIDTH__\"",
        "\"__HEIGHT__\"",
        "\"__FPS__\"",
        "\"__DURATION_SECONDS__\"",
        "\"__SEED__\""
    ];

    private readonly HttpClient httpClient;
    private readonly ComfyUiVideoOptions options;
    private readonly IGpuResourceGate gpuResourceGate;
    private readonly IIdentityAssetRegistry identityAssetRegistry;
    private readonly IIdentityAssetStore identityAssetStore;
    private readonly IAssetFileStore assetFileStore;
    private readonly IMediaInspector mediaInspector;

    public ComfyUiVideoGenerationProvider(
        HttpClient httpClient,
        IOptions<ComfyUiVideoOptions> options,
        IGpuResourceGate gpuResourceGate,
        IIdentityAssetRegistry identityAssetRegistry,
        IIdentityAssetStore identityAssetStore,
        IAssetFileStore assetFileStore,
        IMediaInspector mediaInspector)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.gpuResourceGate = gpuResourceGate;
        this.identityAssetRegistry = identityAssetRegistry;
        this.identityAssetStore = identityAssetStore;
        this.assetFileStore = assetFileStore;
        this.mediaInspector = mediaInspector;
    }

    public bool IsEnabled => options.Enabled;

    public async Task<VideoGenerationResult> GenerateAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled)
        {
            throw new VideoGenerationException(
                VideoGenerationException.NotConfigured,
                "The ComfyUI video engine is not enabled.");
        }

        ValidateRequest(request);

        var configuredWorkflowPath = request.FirstFrame is null
            ? options.WorkflowPath
            : options.FirstFrameWorkflowPath;
        var workflowPath = ResolveWorkflowPath(configuredWorkflowPath);
        if (!File.Exists(workflowPath))
        {
            throw new VideoGenerationException(
                VideoGenerationException.NotConfigured,
                $"The ComfyUI video workflow '{configuredWorkflowPath}' was not found.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            string? uploadedReference = null;
            if (request.FirstFrame is not null)
            {
                uploadedReference = await PrepareFirstFrameAsync(
                    request.FirstFrame,
                    timeout.Token);
            }

            var graph = BuildGraph(
                await File.ReadAllTextAsync(workflowPath, timeout.Token),
                request,
                uploadedReference);
            var clientId = Guid.NewGuid().ToString("N");

            byte[] artifact;
            await using (var lease = await gpuResourceGate.AcquireAsync(
                GpuWorkloads.ComfyUi,
                cancellationToken))
            {
                var promptId = await SubmitAsync(graph, clientId, timeout.Token);
                var video = await WaitForArtifactAsync(promptId, timeout.Token);
                artifact = await DownloadAsync(video, timeout.Token);
            }

            ValidateArtifactNotEmpty(artifact);

            // CPU-only from here: the lease above is already released, so persisting
            // and probing never hold the GPU gate.
            var file = await assetFileStore.WriteAsync(
                BuildRelativeOutputPath(request, artifact),
                artifact,
                cancellationToken);

            var inspection = await InspectAsync(file.AbsolutePath, cancellationToken);
            ValidateInspection(request, inspection);

            return new VideoGenerationResult(
                file.RelativePath,
                file.ContentHash,
                file.ByteSize,
                inspection.DurationSeconds,
                inspection.Width,
                inspection.Height,
                request.FramesPerSecond);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new VideoGenerationException(
                VideoGenerationException.Timeout,
                $"ComfyUI exceeded {options.TimeoutSeconds} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "The ComfyUI video engine could not be reached or rejected the request.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "ComfyUI returned an unexpected response.",
                exception);
        }
    }

    private void ValidateRequest(VideoGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw Invalid("The animation prompt is required.");
        }

        if (request.DurationSeconds is < 1 || request.DurationSeconds > options.MaxSeconds)
        {
            throw Invalid($"Duration must be between 1 and {options.MaxSeconds} seconds.");
        }

        if (request.FramesPerSecond is < 1 || request.FramesPerSecond > options.Fps)
        {
            throw Invalid($"Frames per second must be between 1 and {options.Fps}.");
        }

        if (request.Width is < 16 || request.Width > options.Width || request.Width % 2 != 0)
        {
            throw Invalid($"Width must be an even number between 16 and {options.Width}.");
        }

        if (request.Height is < 16 || request.Height > options.Height || request.Height % 2 != 0)
        {
            throw Invalid($"Height must be an even number between 16 and {options.Height}.");
        }
    }

    private async Task<string> PrepareFirstFrameAsync(
        PinnedIdentityAsset reference,
        CancellationToken cancellationToken)
    {
        if (!identityAssetRegistry.TryGet(reference.AssetId, reference.Version, out var metadata)
            || metadata.Id != reference.AssetId
            || metadata.Version != reference.Version)
        {
            throw Invalid(
                $"Identity asset '{reference.AssetId}' v{reference.Version.Value} was not found.");
        }

        if (metadata.Status != IdentityAssetStatus.Approved)
        {
            throw Invalid(
                $"Identity asset '{reference.AssetId}' v{reference.Version.Value} is not approved.");
        }

        if (!string.Equals(metadata.Kind, SupportedReferenceKind, StringComparison.Ordinal)
            || !string.Equals(metadata.MediaType, SupportedReferenceMediaType, StringComparison.Ordinal))
        {
            throw Invalid(
                $"Identity asset '{reference.AssetId}' v{reference.Version.Value} cannot be used as a first frame.");
        }

        if (!identityAssetStore.Exists(reference.AssetId, reference.Version))
        {
            throw Invalid(
                $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} were not found.");
        }

        ReadOnlyMemory<byte> bytes;
        try
        {
            bytes = await identityAssetStore.ReadBytesAsync(
                reference.AssetId,
                reference.Version,
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is AssetCollectionException
                or FileNotFoundException
                or DirectoryNotFoundException)
        {
            throw Invalid(
                $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} were not found.",
                exception);
        }

        var actualHash = Convert
            .ToHexString(SHA256.HashData(bytes.Span))
            .ToLowerInvariant();
        if (bytes.Length != metadata.ByteSize
            || !string.Equals(actualHash, metadata.ContentHash, StringComparison.Ordinal)
            || !IsPng(bytes.Span))
        {
            throw Invalid(
                $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} do not match approved metadata.");
        }

        return await UploadFirstFrameAsync(
            bytes,
            metadata.MediaType,
            BuildUploadFilename(reference, metadata.ContentHash),
            cancellationToken);
    }

    private async Task<string> UploadFirstFrameAsync(
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

        using var response = await httpClient.PostAsync("upload/image", multipart, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                $"ComfyUI rejected the first-frame upload ({(int)response.StatusCode}).");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var upload = JsonNode.Parse(body) as JsonObject;
        var returnedName = upload?["name"] is JsonValue name ? name.GetValue<string>() : null;
        var returnedSubfolder = upload?["subfolder"] is JsonValue subfolder
            ? subfolder.GetValue<string>()
            : string.Empty;
        var returnedType = upload?["type"] is JsonValue type ? type.GetValue<string>() : null;

        if (!string.Equals(returnedName, filename, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(returnedSubfolder)
            || !string.Equals(returnedType, "input", StringComparison.Ordinal))
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "ComfyUI returned an unexpected first-frame upload location.");
        }

        return returnedName!;
    }

    /// <summary>
    /// Injects bounded values into one fixed repository-owned workflow. Values are
    /// JSON-encoded, so neither the prompt nor the uploaded filename can alter graph
    /// shape. Placeholder names are the contract with the workflow file authored in
    /// TODO(verify-on-home).
    /// </summary>
    private JsonNode BuildGraph(
        string template,
        VideoGenerationRequest request,
        string? uploadedReference)
    {
        // A missing required placeholder or a first-frame node in the text-only graph
        // means the configured workflow does not match the documented contract, so the
        // trusted graph is rejected instead of submitted in a broken state.
        if (RequiredPlaceholders.Any(token => !template.Contains(token, StringComparison.Ordinal))
            || (uploadedReference is null
                && template.Contains("\"__REFERENCE_IMAGE__\"", StringComparison.Ordinal))
            || (uploadedReference is not null
                && !template.Contains("\"__REFERENCE_IMAGE__\"", StringComparison.Ordinal)))
        {
            throw new VideoGenerationException(
                VideoGenerationException.NotConfigured,
                "The ComfyUI video workflow does not match the expected placeholder contract.");
        }

        var text = template
            .Replace("\"__PROMPT__\"", JsonString(request.Prompt), StringComparison.Ordinal)
            .Replace("\"__WIDTH__\"", request.Width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("\"__HEIGHT__\"", request.Height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("\"__FPS__\"", request.FramesPerSecond.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("\"__DURATION_SECONDS__\"", request.DurationSeconds.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("\"__SEED__\"", NormalizeSeed(request.Seed), StringComparison.Ordinal);

        if (uploadedReference is not null)
        {
            text = text.Replace(
                "\"__REFERENCE_IMAGE__\"",
                JsonString(uploadedReference),
                StringComparison.Ordinal);
        }

        return JsonNode.Parse(text)
            ?? throw new VideoGenerationException(
                VideoGenerationException.NotConfigured,
                "The ComfyUI video workflow template is not valid JSON.");
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
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                $"ComfyUI rejected the video workflow ({(int)response.StatusCode}).");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var promptId = (JsonNode.Parse(responseBody) as JsonObject)?["prompt_id"] is JsonValue value
            ? value.GetValue<string>()
            : null;

        if (string.IsNullOrWhiteSpace(promptId))
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "ComfyUI did not return a prompt id.");
        }

        return promptId;
    }

    private async Task<ComfyVideo> WaitForArtifactAsync(
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
                && status["completed"] is JsonValue completed
                && completed.GetValue<bool>())
            {
                var statusText = status["status_str"] is JsonValue statusValue
                    ? statusValue.GetValue<string>()
                    : null;
                if (!string.Equals(statusText, "success", StringComparison.Ordinal))
                {
                    throw new VideoGenerationException(
                        VideoGenerationException.GenerationFailed,
                        $"ComfyUI reported status '{statusText}'.");
                }

                return FindArtifact(job)
                    ?? throw new VideoGenerationException(
                        VideoGenerationException.GenerationFailed,
                        "ComfyUI completed without producing a video artifact.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Scans every output array for a filename entry instead of assuming an
    /// output-node key name, so no unverified ComfyUI node contract is encoded here.
    /// </summary>
    private static ComfyVideo? FindArtifact(JsonObject job)
    {
        if (job["outputs"] is not JsonObject outputs)
        {
            return null;
        }

        foreach (var (_, output) in outputs)
        {
            if (output is not JsonObject node)
            {
                continue;
            }

            foreach (var (_, value) in node)
            {
                if (value is not JsonArray entries)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (entry is not JsonObject file)
                    {
                        continue;
                    }

                    var filename = file["filename"] is JsonValue name
                        ? name.GetValue<string>()
                        : null;
                    if (string.IsNullOrWhiteSpace(filename))
                    {
                        continue;
                    }

                    return new ComfyVideo(
                        filename,
                        file["subfolder"] is JsonValue subfolder
                            ? subfolder.GetValue<string>() ?? string.Empty
                            : string.Empty,
                        file["type"] is JsonValue type
                            ? type.GetValue<string>() ?? "output"
                            : "output");
                }
            }
        }

        return null;
    }

    private async Task<byte[]> DownloadAsync(
        ComfyVideo video,
        CancellationToken cancellationToken)
    {
        var query =
            $"view?filename={Uri.EscapeDataString(video.Filename)}"
            + $"&subfolder={Uri.EscapeDataString(video.Subfolder)}"
            + $"&type={Uri.EscapeDataString(video.Type)}";

        using var response = await httpClient.GetAsync(query, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                $"ComfyUI video download failed ({(int)response.StatusCode}).");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static void ValidateArtifactNotEmpty(byte[] artifact)
    {
        if (artifact.Length == 0)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "ComfyUI returned an empty video artifact.");
        }
    }

    private async Task<MediaInspection> InspectAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mediaInspector.InspectAsync(absolutePath, cancellationToken);
        }
        catch (ProcessExecutionException exception)
        {
            // A probe that cannot run or fails to parse is not a usable artifact; the
            // closed I2V error contract has no separate probe code.
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "ComfyUI returned a video artifact that could not be probed.",
                exception);
        }
    }

    /// <summary>
    /// The probe is the single artifact authority (no container-signature check is kept).
    /// It requires a real video stream, a duration within
    /// <c>max(0.25 s, 5% of the request)</c>, and exactly the requested dimensions. Fps
    /// is intentionally not compared: <see cref="IMediaInspector"/> does not report it
    /// and frame-rate values drift with encoder rounding; the request fps is already
    /// validated against the trusted upper bound.
    /// </summary>
    private static void ValidateInspection(
        VideoGenerationRequest request,
        MediaInspection inspection)
    {
        if (!inspection.HasVideo)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "ComfyUI returned an artifact without a video stream.");
        }

        if (inspection.DurationSeconds <= 0)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "ComfyUI returned a video artifact with no duration.");
        }

        var durationTolerance = Math.Max(
            MinimumDurationToleranceSeconds,
            request.DurationSeconds * DurationToleranceRatio);
        if (Math.Abs(inspection.DurationSeconds - request.DurationSeconds) > durationTolerance)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "ComfyUI returned a video artifact whose duration does not match the request.");
        }

        if (inspection.Width != request.Width || inspection.Height != request.Height)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "ComfyUI returned a video artifact with unexpected dimensions.");
        }
    }

    private string BuildRelativeOutputPath(VideoGenerationRequest request, byte[] artifact)
    {
        var fingerprint = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join(
                    '|',
                    request.Prompt,
                    request.DurationSeconds.ToString(CultureInfo.InvariantCulture),
                    request.FramesPerSecond.ToString(CultureInfo.InvariantCulture),
                    request.Width.ToString(CultureInfo.InvariantCulture),
                    request.Height.ToString(CultureInfo.InvariantCulture),
                    request.Seed.ToString(CultureInfo.InvariantCulture),
                    request.FirstFrame?.AssetId.Value ?? "none",
                    request.FirstFrame?.Version.Value.ToString(CultureInfo.InvariantCulture) ?? "none"))))
            .ToLowerInvariant()[..16];

        // Naming hint only, never a gate: the probe above decides whether the artifact
        // is accepted, so a WebM container keeps a truthful extension.
        var extension = IsWebM(artifact) ? "webm" : "mp4";
        return $"{options.OutputDirectory.TrimEnd('/')}/i2v-{fingerprint}.{extension}";
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

    private static VideoGenerationException Invalid(
        string message,
        Exception? innerException = null) =>
        new(VideoGenerationException.RequestInvalid, message, innerException);

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

    private static bool IsWebM(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4
        && bytes[0] == 0x1A
        && bytes[1] == 0x45
        && bytes[2] == 0xDF
        && bytes[3] == 0xA3;

    private sealed record ComfyVideo(string Filename, string Subfolder, string Type);
}
