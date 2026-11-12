using System.Net;
using System.Text;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Rendering;
using AIStudio.Infrastructure.AI.Gemini;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.AI;

public sealed class GeminiTextGeneratorTests
{
    [Fact]
    public void GeminiOptions_DefaultValues_AreExpected()
    {
        var options = new GeminiOptions();

        Assert.False(options.Enabled);
        Assert.Equal("https://generativelanguage.googleapis.com/", options.BaseUrl);
        Assert.Equal(string.Empty, options.ApiKey);
        Assert.Equal("gemini-2.5-flash", options.DefaultModel);
        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Equal("Gemini", GeminiOptions.SectionName);
    }

    [Theory]
    [InlineData(false, "valid-key")]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task GenerateAsync_DisabledOrMissingApiKey_ThrowsProviderUnavailable(
        bool enabled,
        string apiKey)
    {
        var generator = CreateGenerator(
            new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("{}"))),
            new GeminiOptions
            {
                Enabled = enabled,
                ApiKey = apiKey
            });

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Hello"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.ProviderUnavailable, exception.ErrorCode);
        Assert.Equal("The Gemini text generator is not enabled or configured.", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_ValidTextGeneration_SendsExpectedHeadersAndBody_AndParsesResponse()
    {
        string? requestBody = null;
        Uri? requestUri = null;
        IEnumerable<string>? apiKeyHeaders = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestUri = request.RequestUri;
            apiKeyHeaders = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values : null;
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return JsonResponse(
                """
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "{\"concept\":\"gravity\",\"description\":\"attraction\"}" }
                        ],
                        "role": "model"
                      },
                      "finishReason": "STOP"
                    }
                  ],
                  "usageMetadata": {
                    "promptTokenCount": 15,
                    "candidatesTokenCount": 25,
                    "totalTokenCount": 40
                  },
                  "modelVersion": "gemini-2.5-flash-001"
                }
                """);
        });

        var generator = CreateGenerator(handler);

        var response = await generator.GenerateAsync(
            new AiTextRequest(
                "Explain gravity.",
                "You are a physics professor.",
                "gemini-2.5-flash",
                AiResponseFormat.JsonObject,
                Temperature: 0.7,
                MaxTokens: 256),
            TestContext.Current.CancellationToken);

        Assert.NotNull(requestUri);
        Assert.Equal(new Uri("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent"), requestUri);
        Assert.True(string.IsNullOrEmpty(requestUri.Query));

        Assert.NotNull(apiKeyHeaders);
        Assert.Equal("test-secret-key-12345", Assert.Single(apiKeyHeaders));

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;

        var contents = root.GetProperty("contents");
        Assert.Equal(1, contents.GetArrayLength());
        Assert.Equal("user", contents[0].GetProperty("role").GetString());
        Assert.Equal("Explain gravity.", contents[0].GetProperty("parts")[0].GetProperty("text").GetString());

        var systemInstruction = root.GetProperty("systemInstruction");
        Assert.Equal("You are a physics professor.", systemInstruction.GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.False(systemInstruction.TryGetProperty("role", out _));

        var generationConfig = root.GetProperty("generationConfig");
        Assert.Equal("application/json", generationConfig.GetProperty("responseMimeType").GetString());
        Assert.Equal(0.7, generationConfig.GetProperty("temperature").GetDouble());
        Assert.Equal(256, generationConfig.GetProperty("maxOutputTokens").GetInt32());

        Assert.Equal("{\"concept\":\"gravity\",\"description\":\"attraction\"}", response.Text);
        Assert.Equal("gemini-2.5-flash-001", response.Model);
        Assert.Equal(15, response.PromptTokenCount);
        Assert.Equal(25, response.OutputTokenCount);
        Assert.NotNull(response.TotalDuration);
        Assert.True(response.TotalDuration > TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateAsync_PlainTextGeneration_OmitsSystemInstructionAndGenerationConfig()
    {
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                """
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "Hello user" }
                        ],
                        "role": "model"
                      }
                    }
                  ],
                  "modelVersion": "gemini-2.5-flash"
                }
                """);
        });

        var generator = CreateGenerator(handler);

        var response = await generator.GenerateAsync(
            new AiTextRequest("Hello"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("systemInstruction", out _));
        Assert.False(root.TryGetProperty("generationConfig", out _));

        Assert.Equal("Hello user", response.Text);
        Assert.Equal("gemini-2.5-flash", response.Model);
    }

    [Fact]
    public async Task GenerateAsync_ConcatenatesMultipleContentParts()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "Hello " },
                          { "text": "world!" }
                        ]
                      }
                    }
                  ]
                }
                """)));

        var generator = CreateGenerator(handler);

        var response = await generator.GenerateAsync(
            new AiTextRequest("Hello"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Hello world!", response.Text);
        Assert.Equal("gemini-2.5-flash", response.Model);
    }

    [Fact]
    public async Task GenerateAsync_JsonObjectMode_ThrowsOnMalformedJson()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "not a valid json object" }
                        ]
                      }
                    }
                  ]
                }
                """)));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(
                new AiTextRequest("Return JSON", ResponseFormat: AiResponseFormat.JsonObject),
                TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.MalformedResponse, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_JsonObjectMode_ThrowsOnJsonArray()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "[1, 2, 3]" }
                        ]
                      }
                    }
                  ]
                }
                """)));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(
                new AiTextRequest("Return JSON", ResponseFormat: AiResponseFormat.JsonObject),
                TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.MalformedResponse, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_NoCandidates_ThrowsProviderError()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """
                {
                  "candidates": []
                }
                """)));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Hello"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.ProviderError, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_NoContentParts_ThrowsMalformedResponse()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": []
                      }
                    }
                  ]
                }
                """)));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Hello"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.MalformedResponse, exception.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, AiErrorCode.ProviderUnavailable, 429)]
    [InlineData(HttpStatusCode.InternalServerError, AiErrorCode.ProviderError, 500)]
    [InlineData(HttpStatusCode.BadRequest, AiErrorCode.InvalidRequest, 400)]
    [InlineData(HttpStatusCode.NotFound, AiErrorCode.ModelNotFound, 404)]
    public async Task GenerateAsync_MapsProviderHttpErrors_NeverLeaksApiKey(
        HttpStatusCode statusCode,
        AiErrorCode expectedErrorCode,
        int expectedStatusCode)
    {
        const string secretKey = "test-secret-key-12345";
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                $$"""
                {
                  "error": {
                    "code": {{expectedStatusCode}},
                    "message": "Resource failure for key {{secretKey}}",
                    "status": "FAILED"
                  }
                }
                """,
                statusCode)));

        var generator = CreateGenerator(
            handler,
            new GeminiOptions
            {
                Enabled = true,
                ApiKey = secretKey,
                DefaultModel = "gemini-2.5-flash"
            });

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Hello"), TestContext.Current.CancellationToken));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
        Assert.Equal(expectedStatusCode, exception.ProviderStatusCode);
        Assert.DoesNotContain(secretKey, exception.Message);
        Assert.Contains("[REDACTED]", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_MapsClientTimeout()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{}");
        });

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
            Timeout = TimeSpan.FromMilliseconds(50)
        };
        var generator = CreateGenerator(client);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Hello"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.Timeout, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_PropagatesCallerCancellation()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{}");
        });

        var generator = CreateGenerator(handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            generator.GenerateAsync(new AiTextRequest("Hello"), cancellation.Token));
    }

    [Fact]
    public async Task GenerateAsync_MapsConnectionFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Connection refused."));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Hello"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.ProviderUnavailable, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_ValidatesRequestArguments()
    {
        var generator = CreateGenerator(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("{}"))));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            generator.GenerateAsync(null!, TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            generator.GenerateAsync(new AiTextRequest(""), TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            generator.GenerateAsync(new AiTextRequest("   "), TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt", Temperature: -0.1), TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt", Temperature: 2.1), TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt", MaxTokens: 0), TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt", ResponseFormat: (AiResponseFormat)999), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_DoesNotDependOnGpuResourceGate()
    {
        var constructor = typeof(GeminiTextGenerator).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(typeof(IGpuResourceGate), parameterTypes);
        Assert.Contains(typeof(HttpClient), parameterTypes);
        Assert.Contains(typeof(IOptions<GeminiOptions>), parameterTypes);
        Assert.Contains(typeof(ILogger<GeminiTextGenerator>), parameterTypes);
    }

    private static GeminiTextGenerator CreateGenerator(
        HttpMessageHandler handler,
        GeminiOptions? options = null) =>
        CreateGenerator(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
                Timeout = TimeSpan.FromSeconds(5)
            },
            options);

    private static GeminiTextGenerator CreateGenerator(
        HttpClient client,
        GeminiOptions? options = null) =>
        new(
            client,
            Options.Create(options ?? new GeminiOptions
            {
                Enabled = true,
                ApiKey = "test-secret-key-12345",
                DefaultModel = "gemini-2.5-flash",
                TimeoutSeconds = (int)client.Timeout.TotalSeconds
            }),
            NullLogger<GeminiTextGenerator>.Instance);

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

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
