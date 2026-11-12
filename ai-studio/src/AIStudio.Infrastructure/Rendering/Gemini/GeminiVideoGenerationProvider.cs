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
/// Cloud-based Google Veo video generation provider implementing <see cref="IVideoGenerationProvider"/>.
/// Submits an asynchronous prediction to the Google Generative Language API, polls the resulting
/// long-running operation, downloads the rendered video artifact, persists it to application storage,
/// and validates the result via <see cref="IMediaInspector"/>. Runs in Google Cloud and does not acquire
/// a local GPU lease.
/// </summary>
public sealed class GeminiVideoGenerationProvider : IVideoGenerationProvider
{
    private const string SupportedReferenceKind = "reference-image";
    private const string SupportedReferenceMediaType = "image/png";
    private const int MaxReferenceByteSize = 8 * 1024 * 1024; // 8 MiB
    private const double MinimumDurationToleranceSeconds = 0.25;
    private const double DurationToleranceRatio = 0.05;

    private readonly HttpClient httpClient;
    private readonly GeminiVideoOptions options;
    private readonly IIdentityAssetRegistry identityAssetRegistry;
    private readonly IIdentityAssetStore identityAssetStore;
    private readonly IAssetFileStore assetFileStore;
    private readonly IMediaInspector mediaInspector;

    public GeminiVideoGenerationProvider(
        HttpClient httpClient,
        IOptions<GeminiVideoOptions> options,
        IIdentityAssetRegistry identityAssetRegistry,
        IIdentityAssetStore identityAssetStore,
        IAssetFileStore assetFileStore,
        IMediaInspector mediaInspector)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identityAssetRegistry);
        ArgumentNullException.ThrowIfNull(identityAssetStore);
        ArgumentNullException.ThrowIfNull(assetFileStore);
        ArgumentNullException.ThrowIfNull(mediaInspector);

        this.httpClient = httpClient;
        this.options = options.Value;
        this.identityAssetRegistry = identityAssetRegistry;
        this.identityAssetStore = identityAssetStore;
        this.assetFileStore = assetFileStore;
        this.mediaInspector = mediaInspector;
    }

    public bool IsEnabled => options.Enabled && !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<VideoGenerationResult> GenerateAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled
            || string.IsNullOrWhiteSpace(options.ApiKey)
            || !IsSafeOutputDirectory(options.OutputDirectory))
        {
            throw new VideoGenerationException(
                VideoGenerationException.NotConfigured,
                "The Gemini Veo video engine is not configured, disabled, or has an unsafe output directory.");
        }

        ValidateRequest(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            string? firstFrameBase64 = null;
            if (request.FirstFrame is not null)
            {
                firstFrameBase64 = await PrepareFirstFrameAsync(
                    request.FirstFrame,
                    timeout.Token);
            }

            var operationName = await SubmitPredictLongRunningAsync(
                request,
                firstFrameBase64,
                timeout.Token);

            var completedOperation = await WaitForOperationAsync(
                operationName,
                timeout.Token);

            var videoUri = ExtractVideoUri(completedOperation);
            if (string.IsNullOrWhiteSpace(videoUri))
            {
                throw new VideoGenerationException(
                    VideoGenerationException.GenerationFailed,
                    "Gemini Veo completed without returning a video URI.");
            }

            var artifact = await DownloadVideoAsync(videoUri, timeout.Token);
            ValidateArtifactNotEmpty(artifact);

            var outputPath = $"{options.OutputDirectory.TrimEnd('/')}/scene-{Guid.NewGuid():N}.mp4";
            var file = await assetFileStore.WriteAsync(outputPath, artifact, cancellationToken);

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
        catch (VideoGenerationException)
        {
            throw;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new VideoGenerationException(
                VideoGenerationException.Timeout,
                $"Gemini Veo generation exceeded {options.TimeoutSeconds} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "The Gemini Veo engine could not be reached or rejected the request.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "Gemini Veo returned an unexpected response.",
                exception);
        }
    }

    private void ValidateRequest(VideoGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Contains('\0'))
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                "The animation prompt is required and must not contain null characters.");
        }

        if (request.DurationSeconds < 1 || request.DurationSeconds > options.MaxSeconds)
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Duration must be between 1 and {options.MaxSeconds} seconds.");
        }

        if (request.FramesPerSecond < 1 || request.FramesPerSecond > options.Fps)
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Frames per second must be between 1 and {options.Fps}.");
        }

        if (request.Width < 16 || request.Width > options.Width || request.Width % 2 != 0)
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Width must be an even number between 16 and {options.Width}.");
        }

        if (request.Height < 16 || request.Height > options.Height || request.Height % 2 != 0)
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Height must be an even number between 16 and {options.Height}.");
        }

        if (request.Seed < 0)
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                "Seed must be non-negative.");
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
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Identity asset '{reference.AssetId}' v{reference.Version.Value} was not found.");
        }

        if (metadata.Status != IdentityAssetStatus.Approved)
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Identity asset '{reference.AssetId}' v{reference.Version.Value} is not approved.");
        }

        if (!string.Equals(metadata.Kind, SupportedReferenceKind, StringComparison.Ordinal)
            || !string.Equals(metadata.MediaType, SupportedReferenceMediaType, StringComparison.Ordinal))
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Identity asset '{reference.AssetId}' v{reference.Version.Value} cannot be used as a first frame.");
        }

        if (!identityAssetStore.Exists(reference.AssetId, reference.Version))
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
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
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} were not found.",
                exception);
        }

        if (bytes.Length > MaxReferenceByteSize || bytes.Length != metadata.ByteSize)
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} have invalid size.");
        }

        var actualHash = Convert
            .ToHexString(SHA256.HashData(bytes.Span))
            .ToLowerInvariant();

        if (!string.Equals(actualHash, metadata.ContentHash, StringComparison.Ordinal)
            || !IsPng(bytes.Span))
        {
            throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Stored bytes for identity asset '{reference.AssetId}' v{reference.Version.Value} do not match approved metadata or are not valid PNG.");
        }

        return Convert.ToBase64String(bytes.Span);
    }

    private async Task<string> SubmitPredictLongRunningAsync(
        VideoGenerationRequest request,
        string? firstFrameBase64,
        CancellationToken cancellationToken)
    {
        var instance = new JsonObject
        {
            ["prompt"] = request.Prompt.Trim()
        };

        if (firstFrameBase64 is not null)
        {
            instance["image"] = new JsonObject
            {
                ["bytesBase64Encoded"] = firstFrameBase64,
                ["mimeType"] = SupportedReferenceMediaType
            };
        }

        var payload = new JsonObject
        {
            ["instances"] = new JsonArray { instance },
            ["parameters"] = new JsonObject
            {
                ["aspectRatio"] = options.AspectRatio,
                ["resolution"] = options.Resolution,
                ["durationSeconds"] = request.DurationSeconds,
                ["sampleCount"] = 1,
                ["seed"] = request.Seed
            }
        };

        var postPath = $"v1beta/models/{Uri.EscapeDataString(options.Model)}:predictLongRunning";
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, postPath)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        postRequest.Headers.Add("x-goog-api-key", options.ApiKey);

        using var response = await httpClient.SendAsync(postRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                $"Gemini Veo rejected generation request ({(int)response.StatusCode}).");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = JsonNode.Parse(body) as JsonObject
            ?? throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "Gemini Veo returned an invalid JSON response.");

        if (json["error"] is JsonObject errorObj)
        {
            var msg = errorObj["message"] is JsonValue msgVal ? msgVal.GetValue<string>() : "Unknown error";
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                $"Gemini Veo generation failed: {Sanitize(msg)}");
        }

        var operationName = json["name"] is JsonValue nameVal
            ? nameVal.GetValue<string>()
            : null;

        if (string.IsNullOrWhiteSpace(operationName) || !IsSafeRelativeOperationPath(operationName))
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "Gemini Veo returned an invalid or unsafe operation name.");
        }

        return operationName;
    }

    private async Task<JsonObject> WaitForOperationAsync(
        string operationName,
        CancellationToken cancellationToken)
    {
        var trimmedName = operationName.Trim().TrimStart('/');
        var pollPath = trimmedName.StartsWith("v1beta/", StringComparison.OrdinalIgnoreCase)
            ? trimmedName
            : $"v1beta/{trimmedName}";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, pollPath);
            pollRequest.Headers.Add("x-goog-api-key", options.ApiKey);

            using var pollResponse = await httpClient.SendAsync(pollRequest, cancellationToken);
            if (!pollResponse.IsSuccessStatusCode)
            {
                throw new VideoGenerationException(
                    VideoGenerationException.GenerationFailed,
                    $"Gemini Veo polling failed ({(int)pollResponse.StatusCode}).");
            }

            var pollBody = await pollResponse.Content.ReadAsStringAsync(cancellationToken);
            var pollJson = JsonNode.Parse(pollBody) as JsonObject
                ?? throw new VideoGenerationException(
                    VideoGenerationException.GenerationFailed,
                    "Gemini Veo returned an invalid polling response.");

            if (pollJson["error"] is JsonObject errorObj)
            {
                var msg = errorObj["message"] is JsonValue msgVal ? msgVal.GetValue<string>() : "Unknown error";
                throw new VideoGenerationException(
                    VideoGenerationException.GenerationFailed,
                    $"Gemini Veo operation reported an error: {Sanitize(msg)}");
            }

            if (pollJson["done"] is JsonValue doneVal && doneVal.GetValue<bool>())
            {
                return pollJson;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Max(1, options.PollIntervalMilliseconds)),
                cancellationToken);
        }
    }

    private async Task<byte[]> DownloadVideoAsync(
        string videoUri,
        CancellationToken cancellationToken)
    {
        Uri targetUri;
        if (Uri.TryCreate(videoUri, UriKind.Absolute, out var absUri))
        {
            if (httpClient.BaseAddress is null
                || !string.Equals(absUri.Host, httpClient.BaseAddress.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new VideoGenerationException(
                    VideoGenerationException.GenerationFailed,
                    "The video download URI host is untrusted or does not match the configured engine host.");
            }

            targetUri = absUri;
        }
        else if (Uri.TryCreate(videoUri, UriKind.Relative, out var relUri))
        {
            if (videoUri.Contains("..", StringComparison.Ordinal) || videoUri.Contains('\\'))
            {
                throw new VideoGenerationException(
                    VideoGenerationException.GenerationFailed,
                    "The video download URI contains invalid path segments.");
            }

            targetUri = relUri;
        }
        else
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                "The video download URI could not be parsed.");
        }

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, targetUri);
        downloadRequest.Headers.Add("x-goog-api-key", options.ApiKey);

        using var downloadResponse = await httpClient.SendAsync(downloadRequest, cancellationToken);
        if (!downloadResponse.IsSuccessStatusCode)
        {
            throw new VideoGenerationException(
                VideoGenerationException.GenerationFailed,
                $"Gemini Veo video download failed ({(int)downloadResponse.StatusCode}).");
        }

        return await downloadResponse.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static string? ExtractVideoUri(JsonObject operation)
    {
        var response = operation["response"] as JsonObject ?? operation;

        if (response["generateVideoResponse"] is JsonObject genResponse
            && genResponse["generatedSamples"] is JsonArray samples
            && samples.Count > 0
            && samples[0] is JsonObject sample
            && sample["video"] is JsonObject video
            && video["uri"] is JsonValue uriVal1
            && uriVal1.TryGetValue<string>(out var u1)
            && !string.IsNullOrWhiteSpace(u1))
        {
            return u1;
        }

        if (response["videos"] is JsonArray videos
            && videos.Count > 0
            && videos[0] is JsonObject videoObj
            && videoObj["uri"] is JsonValue uriVal2
            && uriVal2.TryGetValue<string>(out var u2)
            && !string.IsNullOrWhiteSpace(u2))
        {
            return u2;
        }

        return null;
    }

    private static void ValidateArtifactNotEmpty(byte[] artifact)
    {
        if (artifact.Length == 0)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "Gemini Veo returned an empty video artifact.");
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
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "Gemini Veo returned a video artifact that could not be probed.",
                exception);
        }
    }

    private static void ValidateInspection(
        VideoGenerationRequest request,
        MediaInspection inspection)
    {
        if (!inspection.HasVideo)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "Gemini Veo returned an artifact without a video stream.");
        }

        if (inspection.DurationSeconds <= 0)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "Gemini Veo returned a video artifact with no duration.");
        }

        var durationTolerance = Math.Max(
            MinimumDurationToleranceSeconds,
            request.DurationSeconds * DurationToleranceRatio);

        if (Math.Abs(inspection.DurationSeconds - request.DurationSeconds) > durationTolerance)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "Gemini Veo returned a video artifact whose duration does not match the request.");
        }

        if (inspection.Width != request.Width || inspection.Height != request.Height)
        {
            throw new VideoGenerationException(
                VideoGenerationException.ArtifactInvalid,
                "Gemini Veo returned a video artifact with unexpected dimensions.");
        }
    }

    private static bool IsSafeOutputDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var normalized = directory.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':'))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
        {
            return false;
        }

        return true;
    }

    private static bool IsSafeRelativeOperationPath(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim().TrimStart('/');
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Contains("..", StringComparison.Ordinal)
            || trimmed.Contains('\\')
            || trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.Contains('?')
            || trimmed.Contains('#'))
        {
            return false;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsAbsoluteUri)
        {
            return false;
        }

        return true;
    }

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

    private string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var sanitized = message;
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            sanitized = sanitized.Replace(options.ApiKey, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        }

        if (httpClient.BaseAddress is not null)
        {
            sanitized = sanitized.Replace(httpClient.BaseAddress.Host, "[REDACTED_HOST]", StringComparison.OrdinalIgnoreCase);
            sanitized = sanitized.Replace(httpClient.BaseAddress.ToString().TrimEnd('/'), "[REDACTED_URL]", StringComparison.OrdinalIgnoreCase);
        }

        return sanitized;
    }
}
