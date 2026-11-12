using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.AI.OpenAiCompatible;

public class OpenAiCompatibleTextGenerator : IAiTextGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient httpClient;
    private readonly OpenAiCompatibleOptions options;
    private readonly ILogger logger;

    public OpenAiCompatibleTextGenerator(
        HttpClient httpClient,
        IOptions<OpenAiCompatibleOptions> options,
        ILogger<OpenAiCompatibleTextGenerator> logger)
        : this(
            httpClient,
            (options ?? throw new ArgumentNullException(nameof(options))).Value
                ?? throw new ArgumentException("Options value cannot be null.", nameof(options)),
            (ILogger)logger)
    {
    }

    public OpenAiCompatibleTextGenerator(
        HttpClient httpClient,
        OpenAiCompatibleOptions options,
        ILogger logger)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                "The OpenAI-compatible provider is not enabled or configured with an API key.");
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
            ? options.DefaultModel?.Trim()
            : request.Model.Trim();

        if (string.IsNullOrEmpty(model))
        {
            throw new ArgumentException("Model is required.", nameof(request));
        }

        var messages = new List<OpenAiChatMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new OpenAiChatMessage("system", request.SystemPrompt.Trim()));
        }
        messages.Add(new OpenAiChatMessage("user", prompt));

        OpenAiResponseFormat? responseFormat = request.ResponseFormat == AiResponseFormat.JsonObject
            ? new OpenAiResponseFormat("json_object")
            : null;

        var payload = new OpenAiChatCompletionRequest(
            Model: model,
            Messages: messages,
            Temperature: request.Temperature,
            MaxTokens: request.MaxTokens,
            ResponseFormat: responseFormat);

        logger.LogDebug(
            "Sending OpenAI-compatible text request for model {Model} with response format {ResponseFormat}.",
            model,
            request.ResponseFormat);

        var normalizedBase = options.BaseUrl.TrimEnd('/');
        var relativePath = normalizedBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? "chat/completions"
            : "v1/chat/completions";

        var requestUri = httpClient.BaseAddress is not null
            ? new Uri(relativePath, UriKind.Relative)
            : new Uri(new Uri(normalizedBase + "/"), relativePath);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateProviderExceptionAsync(
                    response,
                    model,
                    options.ApiKey,
                    cancellationToken);
            }

            OpenAiChatCompletionResponse? providerResponse;
            try
            {
                providerResponse = await response.Content.ReadFromJsonAsync<OpenAiChatCompletionResponse>(
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "OpenAI-compatible provider returned malformed JSON.",
                    innerException: exception);
            }
            catch (NotSupportedException exception)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "OpenAI-compatible provider returned an unsupported response content type.",
                    innerException: exception);
            }

            if (providerResponse is null)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "OpenAI-compatible provider returned an empty response.");
            }

            if (providerResponse.Choices is null || providerResponse.Choices.Count == 0)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "OpenAI-compatible response contained no choices.");
            }

            var choice = providerResponse.Choices[0];
            if (choice.Message is null || string.IsNullOrWhiteSpace(choice.Message.Content))
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "OpenAI-compatible choice contained empty or missing content.");
            }

            var text = choice.Message.Content;

            if (request.ResponseFormat == AiResponseFormat.JsonObject)
            {
                ValidateJsonObject(text);
            }

            var duration = Stopwatch.GetElapsedTime(startTimestamp);
            var responseModel = !string.IsNullOrWhiteSpace(providerResponse.Model)
                ? providerResponse.Model
                : model;

            logger.LogInformation(
                "OpenAI-compatible text generation completed with model {Model}; prompt tokens {PromptTokens}, completion tokens {CompletionTokens}, duration {Duration}.",
                responseModel,
                providerResponse.Usage?.PromptTokens,
                providerResponse.Usage?.CompletionTokens,
                duration);

            return new AiTextResponse(
                text,
                responseModel,
                duration,
                providerResponse.Usage?.PromptTokens,
                providerResponse.Usage?.CompletionTokens);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                "OpenAI-compatible text generation timed out for model {Model}.",
                model);
            throw new AiGenerationException(
                AiErrorCode.Timeout,
                $"OpenAI-compatible request timed out for model '{model}'.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                "OpenAI-compatible provider is unavailable for model {Model}.",
                model);
            throw new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                "OpenAI-compatible provider is unavailable. Verify network connectivity and provider settings.",
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
            "OpenAI-compatible request for model {Model} failed with HTTP {StatusCode}.",
            model,
            statusCode);

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new AiGenerationException(
                AiErrorCode.InvalidRequest,
                providerMessage ?? "OpenAI-compatible provider rejected the generation request.",
                statusCode),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                providerMessage ?? "OpenAI-compatible API key is invalid or unauthorized.",
                statusCode),
            HttpStatusCode.NotFound => new AiGenerationException(
                AiErrorCode.ModelNotFound,
                providerMessage ?? $"OpenAI-compatible model '{model}' was not found.",
                statusCode),
            HttpStatusCode.TooManyRequests => new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                providerMessage ?? "OpenAI-compatible rate limit or quota exceeded.",
                statusCode),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => new AiGenerationException(
                AiErrorCode.Timeout,
                providerMessage ?? "OpenAI-compatible request timed out.",
                statusCode),
            _ => new AiGenerationException(
                AiErrorCode.ProviderError,
                providerMessage ?? $"OpenAI-compatible provider returned HTTP {statusCode}.",
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
            else if (document.RootElement.TryGetProperty("message", out var msgElement)
                && msgElement.ValueKind == JsonValueKind.String)
            {
                var msg = msgElement.GetString();
                if (!string.IsNullOrWhiteSpace(msg))
                {
                    return Sanitize(msg, apiKey);
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

    private static void ValidateJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AiGenerationException(
                    AiErrorCode.MalformedResponse,
                    "Expected JSON object output, but response was not a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw new AiGenerationException(
                AiErrorCode.MalformedResponse,
                "Expected JSON object output, but response was not valid JSON.",
                innerException: exception);
        }
    }
}

internal sealed record OpenAiChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OpenAiChatMessage> Messages,
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
    [property: JsonPropertyName("response_format")] OpenAiResponseFormat? ResponseFormat = null);

internal sealed record OpenAiChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record OpenAiResponseFormat(
    [property: JsonPropertyName("type")] string Type);

internal sealed record OpenAiChatCompletionResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChatChoice>? Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

internal sealed record OpenAiChatChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] OpenAiChatMessageResponse? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

internal sealed record OpenAiChatMessageResponse(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content);

internal sealed record OpenAiUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int? TotalTokens);
