using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Infrastructure.AI.OpenAiCompatible;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.AI;

public sealed class OpenAiCompatibleTextGeneratorTests
{
    [Fact]
    public void OpenAiCompatibleOptions_DefaultValues_AreExpected()
    {
        var options = new OpenAiCompatibleOptions();

        Assert.False(options.Enabled);
        Assert.Equal("https://api.openai.com/v1/", options.BaseUrl);
        Assert.Equal(string.Empty, options.ApiKey);
        Assert.Equal("gpt-4o", options.DefaultModel);
        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Equal("OpenAiCompatible", OpenAiCompatibleOptions.SectionName);
    }

    [Fact]
    public void DeepSeekOptions_DefaultValues_AreExpected()
    {
        var options = new DeepSeekOptions();

        Assert.False(options.Enabled);
        Assert.Equal("https://api.deepseek.com/v1/", options.BaseUrl);
        Assert.Equal(string.Empty, options.ApiKey);
        Assert.Equal("deepseek-chat", options.DefaultModel);
        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Equal("DeepSeek", DeepSeekOptions.SectionName);
    }

    [Fact]
    public void OpenAiOptions_DefaultValues_AreExpected()
    {
        var options = new OpenAiOptions();

        Assert.False(options.Enabled);
        Assert.Equal("https://api.openai.com/v1/", options.BaseUrl);
        Assert.Equal(string.Empty, options.ApiKey);
        Assert.Equal("gpt-4o", options.DefaultModel);
        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Equal("OpenAi", OpenAiOptions.SectionName);
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
            new OpenAiCompatibleOptions
            {
                Enabled = enabled,
                ApiKey = apiKey
            });

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Hello"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.ProviderUnavailable, exception.ErrorCode);
        Assert.Equal("The OpenAI-compatible provider is not enabled or configured with an API key.", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_ValidTextGeneration_SendsExpectedHeadersAndBody_AndParsesResponse()
    {
        string? requestBody = null;
        Uri? requestUri = null;
        AuthenticationHeaderValue? authHeader = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestUri = request.RequestUri;
            authHeader = request.Headers.Authorization;
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return JsonResponse(
                """
                {
                  "id": "chatcmpl-test-123",
                  "model": "gpt-4o-2024-08-06",
                  "choices": [
                    {
                      "index": 0,
                      "message": {
                        "role": "assistant",
                        "content": "{\"concept\":\"gravity\",\"description\":\"attraction\"}"
                      },
                      "finish_reason": "stop"
                    }
                  ],
                  "usage": {
                    "prompt_tokens": 15,
                    "completion_tokens": 25,
                    "total_tokens": 40
                  }
                }
                """);
        });

        var generator = CreateGenerator(handler);

        var response = await generator.GenerateAsync(
            new AiTextRequest(
                "Explain gravity.",
                "You are a physics professor.",
                "gpt-4o",
                AiResponseFormat.JsonObject,
                Temperature: 0.7,
                MaxTokens: 256),
            TestContext.Current.CancellationToken);

        Assert.NotNull(requestUri);
        Assert.Equal(new Uri("https://api.openai.com/v1/chat/completions"), requestUri);

        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader.Scheme);
        Assert.Equal("test-openai-key-12345", authHeader.Parameter);

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;

        Assert.Equal("gpt-4o", root.GetProperty("model").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a physics professor.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Explain gravity.", messages[1].GetProperty("content").GetString());

        var responseFormat = root.GetProperty("response_format");
        Assert.Equal("json_object", responseFormat.GetProperty("type").GetString());

        Assert.Equal(0.7, root.GetProperty("temperature").GetDouble());
        Assert.Equal(256, root.GetProperty("max_tokens").GetInt32());

        Assert.Equal("{\"concept\":\"gravity\",\"description\":\"attraction\"}", response.Text);
        Assert.Equal("gpt-4o-2024-08-06", response.Model);
        Assert.Equal(15, response.PromptTokenCount);
        Assert.Equal(25, response.OutputTokenCount);
        Assert.NotNull(response.TotalDuration);
        Assert.True(response.TotalDuration > TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateAsync_PlainTextGeneration_OmitsSystemMessageAndResponseFormat()
    {
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                """
                {
                  "choices": [
                    {
                      "index": 0,
                      "message": {
                        "role": "assistant",
                        "content": "Hello user"
                      }
                    }
                  ],
                  "model": "gpt-4o"
                }
                """);
        });

        var generator = CreateGenerator(handler);

        var response = await generator.GenerateAsync(
            new AiTextRequest("Hello user"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;

        var messages = root.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("Hello user", messages[0].GetProperty("content").GetString());

        Assert.False(root.TryGetProperty("response_format", out _));
        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("max_tokens", out _));
        Assert.Equal("Hello user", response.Text);
        Assert.Equal("gpt-4o", response.Model);
    }

    [Fact]
    public async Task GenerateAsync_UsesDefaultModel_WhenRequestModelIsOmitted()
    {
        string? sentModel = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            sentModel = doc.RootElement.GetProperty("model").GetString();

            return JsonResponse(
                """
                {
                  "choices": [{ "index": 0, "message": { "content": "OK" } }]
                }
                """);
        });

        var generator = CreateGenerator(
            handler,
            new OpenAiCompatibleOptions
            {
                Enabled = true,
                ApiKey = "key",
                DefaultModel = "fallback-default-model"
            });

        await generator.GenerateAsync(
            new AiTextRequest("Test prompt", Model: null),
            TestContext.Current.CancellationToken);

        Assert.Equal("fallback-default-model", sentModel);
    }

    [Fact]
    public async Task GenerateAsync_Timeout_ThrowsAiTimeout()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new TaskCanceledException("HttpClient timeout triggered."));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.Timeout, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_Http400_ThrowsInvalidRequest_AndSanitizesKey()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """
                {
                  "error": {
                    "message": "Invalid temperature with key test-openai-key-12345",
                    "type": "invalid_request_error"
                  }
                }
                """,
                HttpStatusCode.BadRequest)));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.InvalidRequest, exception.ErrorCode);
        Assert.Equal(400, exception.ProviderStatusCode);
        Assert.DoesNotContain("test-openai-key-12345", exception.Message);
        Assert.Contains("[REDACTED]", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_Http401_ThrowsProviderUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """{ "error": { "message": "Incorrect API key provided" } }""",
                HttpStatusCode.Unauthorized)));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.ProviderUnavailable, exception.ErrorCode);
        Assert.Equal(401, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task GenerateAsync_Http404_ThrowsModelNotFound()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """{ "error": { "message": "Model 'gpt-4o' does not exist" } }""",
                HttpStatusCode.NotFound)));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.ModelNotFound, exception.ErrorCode);
        Assert.Equal(404, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task GenerateAsync_Http429_ThrowsProviderUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """{ "error": { "message": "Rate limit exceeded" } }""",
                HttpStatusCode.TooManyRequests)));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.ProviderUnavailable, exception.ErrorCode);
        Assert.Equal(429, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task GenerateAsync_MalformedJson_ThrowsMalformedResponse()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-valid-json", Encoding.UTF8, "application/json")
            }));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.MalformedResponse, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_EmptyChoicesOrMissingContent_ThrowsMalformedResponse()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("""{ "choices": [] }""")));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(new AiTextRequest("Prompt"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.MalformedResponse, exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_JsonObjectFormatWithInvalidJson_ThrowsMalformedResponse()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """
                {
                  "choices": [{ "index": 0, "message": { "content": "not-a-json-object" } }]
                }
                """)));

        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            generator.GenerateAsync(
                new AiTextRequest("Prompt", ResponseFormat: AiResponseFormat.JsonObject),
                TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.MalformedResponse, exception.ErrorCode);
    }

    [Fact]
    public async Task DeepSeekAndOpenAiGenerators_SubclassesExecuteExpectedly()
    {
        var deepseekOptions = new DeepSeekOptions
        {
            Enabled = true,
            ApiKey = "ds-key-123",
            DefaultModel = "deepseek-chat"
        };
        var dsClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """{ "choices": [{ "index": 0, "message": { "content": "DeepSeek answer" } }] }"""))))
        {
            BaseAddress = new Uri(deepseekOptions.BaseUrl)
        };
        var deepSeekGenerator = new DeepSeekTextGenerator(
            dsClient,
            Options.Create(deepseekOptions),
            NullLogger<DeepSeekTextGenerator>.Instance);

        var dsResponse = await deepSeekGenerator.GenerateAsync(
            new AiTextRequest("Hello DeepSeek"),
            TestContext.Current.CancellationToken);

        Assert.Equal("DeepSeek answer", dsResponse.Text);
        Assert.Equal("deepseek-chat", dsResponse.Model);

        var openAiOptions = new OpenAiOptions
        {
            Enabled = true,
            ApiKey = "openai-key-456",
            DefaultModel = "gpt-4o"
        };
        var oaiClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """{ "choices": [{ "index": 0, "message": { "content": "OpenAI answer" } }] }"""))))
        {
            BaseAddress = new Uri(openAiOptions.BaseUrl)
        };
        var openAiGenerator = new OpenAiTextGenerator(
            oaiClient,
            Options.Create(openAiOptions),
            NullLogger<OpenAiTextGenerator>.Instance);

        var oaiResponse = await openAiGenerator.GenerateAsync(
            new AiTextRequest("Hello OpenAI"),
            TestContext.Current.CancellationToken);

        Assert.Equal("OpenAI answer", oaiResponse.Text);
        Assert.Equal("gpt-4o", oaiResponse.Model);
    }

    private static OpenAiCompatibleTextGenerator CreateGenerator(
        HttpMessageHandler handler,
        OpenAiCompatibleOptions? options = null)
    {
        var effectiveOptions = options ?? new OpenAiCompatibleOptions
        {
            Enabled = true,
            ApiKey = "test-openai-key-12345",
            DefaultModel = "gpt-4o"
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(effectiveOptions.BaseUrl)
        };

        return new OpenAiCompatibleTextGenerator(
            httpClient,
            Options.Create(effectiveOptions),
            NullLogger<OpenAiCompatibleTextGenerator>.Instance);
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> syncHandler)
            : this((request, _) => Task.FromResult(syncHandler(request)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
