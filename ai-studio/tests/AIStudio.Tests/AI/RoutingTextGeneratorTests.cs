using AIStudio.Application.AI;
using AIStudio.Infrastructure.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.AI;

public sealed class RoutingTextGeneratorTests
{
    private readonly SpyTextGenerator ollamaSpy = new("ollama");
    private readonly SpyTextGenerator geminiSpy = new("gemini");
    private readonly SpyTextGenerator deepSeekSpy = new("deepseek");
    private readonly SpyTextGenerator openAiSpy = new("openai");

    [Theory]
    [InlineData("gemini")]
    [InlineData("gemini-text")]
    [InlineData("GEMINI")]
    [InlineData("Gemini-Text")]
    public async Task GenerateAsync_RoutesToGemini_WhenGeminiProviderRequested(string provider)
    {
        var router = CreateRouter();

        var response = await router.GenerateAsync(
            new AiTextRequest("Prompt", Provider: provider),
            TestContext.Current.CancellationToken);

        Assert.Equal("Response from gemini", response.Text);
        Assert.NotNull(geminiSpy.LastRequest);
        Assert.Null(ollamaSpy.LastRequest);
        Assert.Null(deepSeekSpy.LastRequest);
        Assert.Null(openAiSpy.LastRequest);
    }

    [Theory]
    [InlineData("deepseek")]
    [InlineData("DEEPSEEK")]
    [InlineData("DeepSeek")]
    public async Task GenerateAsync_RoutesToDeepSeek_WhenDeepSeekProviderRequested(string provider)
    {
        var router = CreateRouter();

        var response = await router.GenerateAsync(
            new AiTextRequest("Prompt", Provider: provider),
            TestContext.Current.CancellationToken);

        Assert.Equal("Response from deepseek", response.Text);
        Assert.NotNull(deepSeekSpy.LastRequest);
        Assert.Null(geminiSpy.LastRequest);
        Assert.Null(ollamaSpy.LastRequest);
        Assert.Null(openAiSpy.LastRequest);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("chatgpt")]
    [InlineData("OpenAI")]
    [InlineData("CHATGPT")]
    public async Task GenerateAsync_RoutesToOpenAi_WhenOpenAiOrChatGptRequested(string provider)
    {
        var router = CreateRouter();

        var response = await router.GenerateAsync(
            new AiTextRequest("Prompt", Provider: provider),
            TestContext.Current.CancellationToken);

        Assert.Equal("Response from openai", response.Text);
        Assert.NotNull(openAiSpy.LastRequest);
        Assert.Null(geminiSpy.LastRequest);
        Assert.Null(deepSeekSpy.LastRequest);
        Assert.Null(ollamaSpy.LastRequest);
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("local")]
    [InlineData("OLLAMA")]
    [InlineData("LOCAL")]
    public async Task GenerateAsync_RoutesToOllama_WhenOllamaOrLocalRequested(string provider)
    {
        var router = CreateRouter();

        var response = await router.GenerateAsync(
            new AiTextRequest("Prompt", Provider: provider),
            TestContext.Current.CancellationToken);

        Assert.Equal("Response from ollama", response.Text);
        Assert.NotNull(ollamaSpy.LastRequest);
        Assert.Null(geminiSpy.LastRequest);
        Assert.Null(deepSeekSpy.LastRequest);
        Assert.Null(openAiSpy.LastRequest);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsInvalidRequest_WhenUnknownProviderRequested()
    {
        var router = CreateRouter();

        var exception = await Assert.ThrowsAsync<AiGenerationException>(() =>
            router.GenerateAsync(
                new AiTextRequest("Prompt", Provider: "claude-custom"),
                TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.InvalidRequest, exception.ErrorCode);
        Assert.Equal("Unknown AI text provider 'claude-custom'.", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateAsync_FallsBackToDefaultProvider_WhenProviderOmitted(string? provider)
    {
        // When default is Gemini
        var geminiRouter = CreateRouter(defaultProvider: "Gemini");
        var geminiResponse = await geminiRouter.GenerateAsync(
            new AiTextRequest("Prompt", Provider: provider),
            TestContext.Current.CancellationToken);
        Assert.Equal("Response from gemini", geminiResponse.Text);

        // When default is DeepSeek
        var dsRouter = CreateRouter(defaultProvider: "DeepSeek");
        var dsResponse = await dsRouter.GenerateAsync(
            new AiTextRequest("Prompt", Provider: provider),
            TestContext.Current.CancellationToken);
        Assert.Equal("Response from deepseek", dsResponse.Text);

        // When default is OpenAi
        var oaiRouter = CreateRouter(defaultProvider: "OpenAi");
        var oaiResponse = await oaiRouter.GenerateAsync(
            new AiTextRequest("Prompt", Provider: provider),
            TestContext.Current.CancellationToken);
        Assert.Equal("Response from openai", oaiResponse.Text);

        // When default is Ollama or anything else
        var ollamaRouter = CreateRouter(defaultProvider: "Ollama");
        var ollamaResponse = await ollamaRouter.GenerateAsync(
            new AiTextRequest("Prompt", Provider: provider),
            TestContext.Current.CancellationToken);
        Assert.Equal("Response from ollama", ollamaResponse.Text);
    }

    private RoutingTextGenerator CreateRouter(string defaultProvider = "Ollama")
    {
        var options = Options.Create(new AiOptions { Provider = defaultProvider });
        return RoutingTextGenerator.CreateForTesting(
            ollamaSpy,
            geminiSpy,
            deepSeekSpy,
            openAiSpy,
            options);
    }

    private sealed class SpyTextGenerator(string name) : IAiTextGenerator
    {
        public string Name { get; } = name;
        public AiTextRequest? LastRequest { get; private set; }

        public Task<AiTextResponse> GenerateAsync(
            AiTextRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new AiTextResponse(
                $"Response from {Name}",
                Name,
                TimeSpan.FromMilliseconds(50),
                10,
                10));
        }
    }
}
