using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.AI;
using AIStudio.Application.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.AI;

public sealed class OllamaTextGenerator(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    IGpuResourceGate gpuResourceGate,
    ILogger<OllamaTextGenerator> logger) : IAiTextGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OllamaOptions ollamaOptions = options.Value;

    public async Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prompt = RequireText(request.Prompt, nameof(request.Prompt));
        var model = string.IsNullOrWhiteSpace(request.Model)
            ? RequireText(ollamaOptions.DefaultModel, nameof(ollamaOptions.DefaultModel))
            : RequireText(request.Model, nameof(request.Model));

        ValidateGenerationOptions(request);

        var messages = new List<OllamaMessage>(2);
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new OllamaMessage("system", request.SystemPrompt.Trim()));
        }

        messages.Add(new OllamaMessage("user", prompt));

        var generationOptions = request.Temperature is null && request.MaxTokens is null
            ? null
            : new OllamaGenerationOptions(request.Temperature, request.MaxTokens);

        var payload = new OllamaChatRequest(
            model,
            messages,
            Stream: false,
            request.ResponseFormat == AiResponseFormat.JsonObject ? "json" : null,
            generationOptions,
            request.Think);

        logger.LogDebug(
            "Sending Ollama text request for model {Model} with response format {ResponseFormat}.",
            model,
            request.ResponseFormat);

        try
        {
            // The local model occupies the GPU while it generates; share the same
            // exclusive lease as the image and 3D engines.
            await using var lease = await gpuResourceGate.AcquireAsync(
                GpuWorkloads.Ollama,
                cancellationToken);

            using var response = await httpClient.PostAsJsonAsync(
                "api/chat",
                payload,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateProviderExceptionAsync(
                    response,
                    model,
                    cancellationToken);
            }

            OllamaChatResponse? providerResponse;
            try
            {
                providerResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException exception)
            {
                throw MalformedResponse(
                    "Ollama returned malformed JSON.",
                    exception);
            }
            catch (NotSupportedException exception)
            {
                throw MalformedResponse(
                    "Ollama returned an unsupported response content type.",
                    exception);
            }

            if (providerResponse is null
                || !providerResponse.Done
                || string.IsNullOrWhiteSpace(providerResponse.Model)
                || providerResponse.Message?.Content is null)
            {
                throw MalformedResponse(
                    "Ollama response is missing required completion fields.");
            }

            if (request.ResponseFormat == AiResponseFormat.JsonObject)
            {
                ValidateJsonObject(providerResponse.Message.Content);
            }

            var duration = ToTimeSpan(providerResponse.TotalDurationNanoseconds);

            logger.LogInformation(
                "Ollama text generation completed with model {Model}; prompt tokens {PromptTokens}, output tokens {OutputTokens}, duration {Duration}.",
                providerResponse.Model,
                providerResponse.PromptTokenCount,
                providerResponse.OutputTokenCount,
                duration);

            return new AiTextResponse(
                providerResponse.Message.Content,
                providerResponse.Model,
                duration,
                providerResponse.PromptTokenCount,
                providerResponse.OutputTokenCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(
                "Ollama text generation timed out for model {Model}.",
                model);
            throw new AiGenerationException(
                AiErrorCode.Timeout,
                $"Ollama request timed out for model '{model}'.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                "Ollama is unavailable for model {Model}.",
                model);
            throw new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                "Ollama is unavailable. Verify that the local service is running and reachable.",
                exception.StatusCode is null ? null : (int)exception.StatusCode,
                exception);
        }
    }

    private async Task<AiGenerationException> CreateProviderExceptionAsync(
        HttpResponseMessage response,
        string model,
        CancellationToken cancellationToken)
    {
        var providerMessage = await ReadErrorMessageAsync(response, cancellationToken);
        var statusCode = (int)response.StatusCode;

        logger.LogWarning(
            "Ollama request for model {Model} failed with HTTP {StatusCode}.",
            model,
            statusCode);

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new AiGenerationException(
                AiErrorCode.InvalidRequest,
                providerMessage ?? "Ollama rejected the generation request.",
                statusCode),
            HttpStatusCode.NotFound => new AiGenerationException(
                AiErrorCode.ModelNotFound,
                providerMessage ?? $"Ollama model '{model}' was not found.",
                statusCode),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                new AiGenerationException(
                    AiErrorCode.Timeout,
                    providerMessage ?? "Ollama request timed out.",
                    statusCode),
            _ => new AiGenerationException(
                AiErrorCode.ProviderError,
                providerMessage ?? $"Ollama returned HTTP {statusCode}.",
                statusCode)
        };
    }

    private static async Task<string?> ReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                content,
                cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateGenerationOptions(AiTextRequest request)
    {
        if (!Enum.IsDefined(request.ResponseFormat))
        {
            throw new AiGenerationException(
                AiErrorCode.InvalidRequest,
                "AI response format is invalid.");
        }

        if (request.Temperature is < 0 or > 2)
        {
            throw new AiGenerationException(
                AiErrorCode.InvalidRequest,
                "Temperature must be between 0 and 2.");
        }

        if (request.MaxTokens is <= 0)
        {
            throw new AiGenerationException(
                AiErrorCode.InvalidRequest,
                "MaxTokens must be greater than zero.");
        }
    }

    private static void ValidateJsonObject(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw MalformedResponse(
                    "Ollama structured response must be a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw MalformedResponse(
                "Ollama structured response is not valid JSON.",
                exception);
        }
    }

    private static AiGenerationException MalformedResponse(
        string message,
        Exception? innerException = null) =>
        new(AiErrorCode.MalformedResponse, message, innerException: innerException);

    private static TimeSpan? ToTimeSpan(long? nanoseconds)
    {
        if (nanoseconds is null or < 0)
        {
            return null;
        }

        var ticks = nanoseconds.Value / 100;
        return ticks <= TimeSpan.MaxValue.Ticks
            ? TimeSpan.FromTicks(ticks)
            : null;
    }

    private static string RequireText(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new AiGenerationException(
                AiErrorCode.InvalidRequest,
                $"{parameterName} is required.");
        }

        return normalized;
    }
}
