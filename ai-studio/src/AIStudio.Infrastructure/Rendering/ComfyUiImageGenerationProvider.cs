using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Local ComfyUI text-to-image engine. It submits one repository-owned, approved
/// workflow (ComfyUI API format) to the local server and returns the rendered PNG.
/// Only the prompt, image dimensions, and seed are injected; no graph is ever built
/// or supplied by a model. Missing server, timeout, and rejection are mapped to the
/// stable <see cref="RenderVideoException"/> codes the job handler already surfaces.
/// </summary>
public sealed class ComfyUiImageGenerationProvider : IImageGenerationProvider
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly HttpClient httpClient;
    private readonly ComfyUiOptions options;

    public ComfyUiImageGenerationProvider(
        HttpClient httpClient,
        IOptions<ComfyUiOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
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

        var workflowPath = ResolveWorkflowPath();
        if (!File.Exists(workflowPath))
        {
            throw new RenderVideoException(
                "image_workflow_not_found",
                $"ComfyUI workflow '{options.WorkflowPath}' was not found.");
        }

        var clientId = Guid.NewGuid().ToString("N");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            var graph = BuildGraph(
                await File.ReadAllTextAsync(workflowPath, cancellationToken),
                request);
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

    /// <summary>
    /// Injects the prompt, dimensions, and seed into the fixed approved workflow.
    /// The prompt is JSON-encoded before substitution so no prompt text can break
    /// out of the template's JSON string.
    /// </summary>
    private JsonNode BuildGraph(string template, ImageGenerationRequest request)
    {
        var encodedPrompt = "\"" + JsonEncodedText.Encode(request.Prompt).ToString() + "\"";
        var text = template
            .Replace("\"__PROMPT__\"", encodedPrompt, StringComparison.Ordinal)
            .Replace("\"__WIDTH__\"", options.Width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("\"__HEIGHT__\"", options.Height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("\"__SEED__\"", NormalizeSeed(request.Seed), StringComparison.Ordinal);

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

    private string ResolveWorkflowPath()
    {
        if (Path.IsPathRooted(options.WorkflowPath))
        {
            return options.WorkflowPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, options.WorkflowPath),
            Path.Combine(Directory.GetCurrentDirectory(), options.WorkflowPath)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.WorkflowPath));
    }

    private static string NormalizeSeed(long seed) =>
        ((ulong)seed).ToString(CultureInfo.InvariantCulture);

    private static bool IsPng(byte[] bytes) =>
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
