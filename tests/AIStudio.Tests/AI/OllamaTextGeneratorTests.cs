using System.Net;
using System.Text;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Infrastructure;
using AIStudio.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.AI;

public sealed class OllamaTextGeneratorTests
{
    [Fact]
    public void OllamaOptions_DefaultModel_IsValidatedStudioRuntimeModel()
    {
        var options = new OllamaOptions();

        Assert.Equal("qwen3.8:27b-q4_K_M", options.DefaultModel);
    }

    [Fact]
    public async Task GenerateAsync_MapsStructuredRequestAndResponseMetadata()
    {
        string? requestBody = null;
        Uri? requestUri = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestUri = request.RequestUri;
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                """
                {
                  "model": "custom-model",
                  "message": {
                    "role": "assistant",
                    "content": "{\"title\":\"Local AI\"}"
                  },
                  "done": true,
                  "total_duration": 1500000000,
                  "prompt_eval_count": 12,
                  "eval_count": 7
                }
                """);
        });
        var generator = CreateGenerator(handler);

        var response = await generator.GenerateAsync(
            new AiTextRequest(
                "Generate an idea.",
                "Return concise content.",
                "custom-model",
                AiResponseFormat.JsonObject,
                Temperature: 0.2,
                MaxTokens: 128),
            TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("http://127.0.0.1:11434/api/chat"), requestUri);
        Assert.NotNull(requestBody);

        using var requestJson = JsonDocument.Parse(requestBody);
        var root = requestJson.RootElement;
        Assert.Equal("custom-model", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("json", root.GetProperty("format").GetString());
        Assert.Equal(0.2, root.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.Equal(128, root.GetProperty("options").GetProperty("num_predict").GetInt32());

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Return concise content.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());

        Assert.Equal("""{"title":"Local AI"}""", response.Text);
        Assert.Equal("custom-model", response.Model);
        Assert.Equal(TimeSpan.FromSeconds(1.5), response.TotalDuration);
        Assert.Equal(12, response.PromptTokenCount);
        Assert.Equal(7, response.OutputTokenCount);
    }

    [Fact]
    public async Task GenerateAsync_UsesConfiguredDefaultModelForPlainText()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                """
                {
                  "model": "qwen3.8:27b-q4_K_M",
                  "message": { "role": "assistant", "content": "Hello" },
                  "done": true
                }
                """);
        });
        var generator = CreateGenerator(handler);

        var response = await generator.GenerateAsync(
            new AiTextRequest("Hello"),
            TestContext.Current.CancellationToken);

        using var requestJson = JsonDocument.Parse(requestBody!);
        var root = requestJson.RootElement;
        Assert.Equal("qwen3.8:27b-q4_K_M", root.GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("format", out _));
        Assert.False(root.TryGetProperty("options", out _));
        Assert.Equal("Hello", response.Text);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AiErrorCode.InvalidRequest)]
    [InlineData(HttpStatusCode.NotFound, AiErrorCode.ModelNotFound)]
    [InlineData(HttpStatusCode.InternalServerError, AiErrorCode.ProviderError)]
    public async Task GenerateAsync_MapsProviderHttpErrors(
        HttpStatusCode statusCode,
        AiErrorCode expectedError)
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    """{"error":"provider failure"}""",
                    Encoding.UTF8,
                    "application/json")
            }));
        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(
            () => generator.GenerateAsync(
                new AiTextRequest("Hello"),
                TestContext.Current.CancellationToken));

        Assert.Equal(expectedError, exception.ErrorCode);
        Assert.Equal((int)statusCode, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsConnectionFailure()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("Connection refused."));
        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(
            () => generator.GenerateAsync(
                new AiTextRequest("Hello"),
                TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.ProviderUnavailable, exception.ErrorCode);
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
            BaseAddress = new Uri("http://127.0.0.1:11434/"),
            Timeout = TimeSpan.FromMilliseconds(50)
        };
        var generator = CreateGenerator(client);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(
            () => generator.GenerateAsync(
                new AiTextRequest("Hello"),
                TestContext.Current.CancellationToken));

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => generator.GenerateAsync(
                new AiTextRequest("Hello"),
                cancellation.Token));
    }

    [Fact]
    public async Task GenerateAsync_RejectsMalformedStructuredResponse()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(
                """
                {
                  "model": "qwen3.8:27b-q4_K_M",
                  "message": { "role": "assistant", "content": "not-json" },
                  "done": true
                }
                """)));
        var generator = CreateGenerator(handler);

        var exception = await Assert.ThrowsAsync<AiGenerationException>(
            () => generator.GenerateAsync(
                new AiTextRequest(
                    "Return JSON.",
                    ResponseFormat: AiResponseFormat.JsonObject),
                TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.MalformedResponse, exception.ErrorCode);
    }

    [Fact]
    public void AddInfrastructure_BindsConfigurationAndWiresGenerator()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Host=127.0.0.1;Database=test;Username=test;Password=test",
            ["Ai:Provider"] = "Ollama",
            ["Ollama:BaseUrl"] = "http://127.0.0.1:11434",
            ["Ollama:DefaultModel"] = "test-model",
            ["Ollama:TimeoutSeconds"] = "30",
            ["JobWorker:Enabled"] = "false",
            ["JobWorker:PollInterval"] = "00:00:01",
            ["JobWorker:LeaseDuration"] = "00:02:00"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var generator = provider.GetRequiredService<IAiTextGenerator>();
        var options = provider.GetRequiredService<IOptions<OllamaOptions>>().Value;

        Assert.IsType<OllamaTextGenerator>(generator);
        Assert.Equal("http://127.0.0.1:11434", options.BaseUrl);
        Assert.Equal("test-model", options.DefaultModel);
        Assert.Equal(30, options.TimeoutSeconds);
    }

    private static OllamaTextGenerator CreateGenerator(HttpMessageHandler handler) =>
        CreateGenerator(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/"),
            Timeout = TimeSpan.FromSeconds(5)
        });

    private static OllamaTextGenerator CreateGenerator(HttpClient client) =>
        new(
            client,
            Options.Create(new OllamaOptions
            {
                BaseUrl = "http://127.0.0.1:11434",
                DefaultModel = "qwen3.8:27b-q4_K_M",
                TimeoutSeconds = (int)client.Timeout.TotalSeconds
            }),
            NullLogger<OllamaTextGenerator>.Instance);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
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
