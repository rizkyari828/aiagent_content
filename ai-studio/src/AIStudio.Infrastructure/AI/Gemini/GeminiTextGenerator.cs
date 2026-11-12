using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.AI.Gemini;

public sealed class GeminiTextGenerator(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiTextGenerator> logger) : IAiTextGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly GeminiOptions geminiOptions = (options ?? throw new ArgumentNullException(nameof(options))).Value
        ?? throw new ArgumentException("Options value cannot be null.", nameof(options));
    private readonly ILogger<GeminiTextGenerator> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!geminiOptions.Enabled || string.IsNullOrWhiteSpace(geminiOptions.ApiKey))
        {
            throw new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                "The Gemini text generator is not enabled or configured.");
        }

        var prompt = request.Prompt?.Trim();
        if (string.IsNullOrEmpty(prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(request));
        }

        if (request.Temperature is < 0.0 or > 2.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Temperature,
                "Temperature must be between 0.0 and 2.0.");
        }

        if (request.MaxTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaxTokens,
                "MaxTokens must be greater than zero.");
        }

        if (!Enum.IsDefined(request.ResponseFormat))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.ResponseFormat,
                "AI response format is invalid.");
        }

        var model = string.IsNullOrWhiteSpace(request.Model)
            ? geminiOptions.DefaultModel?.Trim()
            : request.Model.Trim();

        if (string.IsNullOrEmpty(model))
        {
            throw new ArgumentException("Model is required.", nameof(request));
        }

        GeminiContent? systemInstruction = null;
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            systemInstruction = new GeminiContent(
                Role: null,
                Parts: [new GeminiPart(request.SystemPrompt.Trim())]);
        }

        var contents = new List<GeminiContent>(1)
        {
            new GeminiContent(
                Role: "user",
                Parts: [new GeminiPart(prompt)])
        };

        GeminiGenerationConfig? generationConfig = null;
        if (request.ResponseFormat == AiResponseFormat.JsonObject
            || request.Temperature is not null
            || request.MaxTokens is not null)
        {
            generationConfig = new GeminiGenerationConfig(
                ResponseMimeType: request.ResponseFormat == AiResponseFormat.JsonObject ? "application/json" : null,
                Temperature: request.Temperature,
                MaxOutputTokens: request.MaxTokens);
        }

        var payload = new GeminiGenerateContentRequest(
            Contents: contents,
            SystemInstruction: systemInstruction,
            GenerationConfig: generationConfig);

        logger.LogDebug(
            "Sending Gemini text request for model {Model} with response format {ResponseFormat}.",
            model,
            request.ResponseFormat);

        var relativePath = $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        var requestUri = httpClient.BaseAddress is not null
            ? new Uri(relativePath, UriKind.Relative)
            : new Uri(new Uri(geminiOptions.BaseUrl.TrimEnd('/') + "/"), relativePath);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Add("x-goog-api-key", geminiOptions.ApiKey.Trim());

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            using var response = await httpClient.SendAsync(
                httpRequest,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateProviderExceptionAsync(
                    response,
                    model,
                    geminiOptions.ApiKey,
                    cancellationToken);
            }

            GeminiGenerateContentResponse? providerResponse;
            try
            {
                providerResponse = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "Gemini returned malformed JSON.",
                    innerException: exception);
            }
            catch (NotSupportedException exception)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "Gemini returned an unsupported response content type.",
                    innerException: exception);
            }

            if (providerResponse is null)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "Gemini returned an empty response.");
            }

            if (providerResponse.Candidates is null || providerResponse.Candidates.Count == 0)
            {
                throw new AiGenerationException(
                    AiErrorCode.ProviderError,
                    "Gemini response contained no candidate completions.");
            }

            var parts = providerResponse.Candidates[0].Content?.Parts;
            if (parts is null || parts.Count == 0)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "Gemini candidate contained no content parts.");
            }

            var textBuilder = new StringBuilder();
            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    textBuilder.Append(part.Text);
                }
            }
            var text = textBuilder.ToString();

            if (request.ResponseFormat == AiResponseFormat.JsonObject)
            {
                ValidateJsonObject(text);
            }

            var duration = Stopwatch.GetElapsedTime(startTimestamp);
            var responseModel = !string.IsNullOrWhiteSpace(providerResponse.ModelVersion)
                ? providerResponse.ModelVersion
                : model;

            logger.LogInformation(
                "Gemini text generation completed with model {Model}; prompt tokens {PromptTokens}, output tokens {OutputTokens}, duration {Duration}.",
                responseModel,
                providerResponse.UsageMetadata?.PromptTokenCount,
                providerResponse.UsageMetadata?.CandidatesTokenCount,
                duration);

            return new AiTextResponse(
                text,
                responseModel,
                duration,
                providerResponse.UsageMetadata?.PromptTokenCount,
                providerResponse.UsageMetadata?.CandidatesTokenCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                "Gemini text generation timed out for model {Model}.",
                model);
            throw new AiGenerationException(
                AiErrorCode.Timeout,
                $"Gemini request timed out for model '{model}'.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                "Gemini is unavailable for model {Model}.",
                model);
            throw new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                "Gemini is unavailable. Verify network connectivity and provider settings.",
                exception.StatusCode is null ? null : (int)exception.StatusCode,
                exception);
        }
    }

    private async Task<AiGenerationException> CreateProviderExceptionAsync(
        HttpResponseMessage response,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var providerMessage = await ReadErrorMessageAsync(response, apiKey, cancellationToken);
        var statusCode = (int)response.StatusCode;

        logger.LogWarning(
            "Gemini request for model {Model} failed with HTTP {StatusCode}.",
            model,
            statusCode);

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new AiGenerationException(
                AiErrorCode.InvalidRequest,
                providerMessage ?? "Gemini rejected the generation request.",
                statusCode),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                providerMessage ?? "Gemini API key is invalid or unauthorized.",
                statusCode),
            HttpStatusCode.NotFound => new AiGenerationException(
                AiErrorCode.ModelNotFound,
                providerMessage ?? $"Gemini model '{model}' was not found.",
                statusCode),
            HttpStatusCode.TooManyRequests => new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                providerMessage ?? "Gemini rate limit or quota exceeded.",
                statusCode),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                new AiGenerationException(
                    AiErrorCode.Timeout,
                    providerMessage ?? "Gemini request timed out.",
                    statusCode),
            _ => new AiGenerationException(
                AiErrorCode.ProviderError,
                providerMessage ?? $"Gemini returned HTTP {statusCode}.",
                statusCode)
        };
    }

    private static async Task<string?> ReadErrorMessageAsync(
        HttpResponseMessage response,
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                string? message = null;
                if (errorElement.ValueKind == JsonValueKind.String)
                {
                    message = errorElement.GetString();
                }
                else if (errorElement.ValueKind == JsonValueKind.Object
                    && errorElement.TryGetProperty("message", out var messageElement)
                    && messageElement.ValueKind == JsonValueKind.String)
                {
                    message = messageElement.GetString();
                }

                if (!string.IsNullOrWhiteSpace(message))
                {
                    return Sanitize(message, apiKey);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Sanitize(string message, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey) && message.Contains(apiKey, StringComparison.Ordinal))
        {
            return message.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);
        }

        return message;
    }

    private static void ValidateJsonObject(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "Gemini structured response must be a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw new AiGenerationException(
                AiErrorCode.MalformedResponse,
                "Gemini structured response is not valid JSON.",
                innerException: exception);
        }
    }
}

internal sealed record GeminiGenerateContentRequest(
    [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
    [property: JsonPropertyName("systemInstruction")] GeminiContent? SystemInstruction = null,
    [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig? GenerationConfig = null);

internal sealed record GeminiContent(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart>? Parts);

internal sealed record GeminiPart(
    [property: JsonPropertyName("text")] string? Text);

internal sealed record GeminiGenerationConfig(
    [property: JsonPropertyName("responseMimeType")] string? ResponseMimeType = null,
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens = null);

internal sealed class GeminiGenerateContentResponse
{
    [JsonPropertyName("candidates")]
    public IReadOnlyList<GeminiCandidate>? Candidates { get; init; }

    [JsonPropertyName("usageMetadata")]
    public GeminiUsageMetadata? UsageMetadata { get; init; }

    [JsonPropertyName("modelVersion")]
    public string? ModelVersion { get; init; }
}

internal sealed class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; init; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; init; }
}

internal sealed class GeminiUsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int? PromptTokenCount { get; init; }

    [JsonPropertyName("candidatesTokenCount")]
    public int? CandidatesTokenCount { get; init; }

    [JsonPropertyName("totalTokenCount")]
    public int? TotalTokenCount { get; init; }
}
