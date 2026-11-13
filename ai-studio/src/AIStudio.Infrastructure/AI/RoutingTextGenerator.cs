using AIStudio.Application.AI;
using AIStudio.Infrastructure.AI.Cli;
using AIStudio.Infrastructure.AI.Gemini;
using AIStudio.Infrastructure.AI.OpenAiCompatible;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.AI;

public sealed class RoutingTextGenerator : IAiTextGenerator
{
    private readonly IAiTextGenerator ollamaGenerator;
    private readonly IAiTextGenerator geminiGenerator;
    private readonly IAiTextGenerator deepSeekGenerator;
    private readonly IAiTextGenerator openAiGenerator;
    private readonly IAiTextGenerator? cliGenerator;
    private readonly IOptions<AiOptions> aiOptions;
    private readonly ILogger<RoutingTextGenerator>? logger;

    public RoutingTextGenerator(
        OllamaTextGenerator ollamaGenerator,
        GeminiTextGenerator geminiGenerator,
        DeepSeekTextGenerator deepSeekGenerator,
        OpenAiTextGenerator openAiGenerator,
        CliTextGenerator cliGenerator,
        IOptions<AiOptions> aiOptions,
        ILogger<RoutingTextGenerator>? logger = null)
    {
        this.ollamaGenerator = ollamaGenerator ?? throw new ArgumentNullException(nameof(ollamaGenerator));
        this.geminiGenerator = geminiGenerator ?? throw new ArgumentNullException(nameof(geminiGenerator));
        this.deepSeekGenerator = deepSeekGenerator ?? throw new ArgumentNullException(nameof(deepSeekGenerator));
        this.openAiGenerator = openAiGenerator ?? throw new ArgumentNullException(nameof(openAiGenerator));
        this.cliGenerator = cliGenerator ?? throw new ArgumentNullException(nameof(cliGenerator));
        this.aiOptions = aiOptions ?? throw new ArgumentNullException(nameof(aiOptions));
        this.logger = logger;
    }

    public static RoutingTextGenerator CreateForTesting(
        IAiTextGenerator ollamaGenerator,
        IAiTextGenerator geminiGenerator,
        IAiTextGenerator deepSeekGenerator,
        IAiTextGenerator openAiGenerator,
        IOptions<AiOptions> aiOptions,
        ILogger<RoutingTextGenerator>? logger = null,
        IAiTextGenerator? cliGenerator = null) =>
        new(
            ollamaGenerator,
            geminiGenerator,
            deepSeekGenerator,
            openAiGenerator,
            cliGenerator,
            aiOptions,
            logger,
            isTest: true);

    private RoutingTextGenerator(
        IAiTextGenerator ollamaGenerator,
        IAiTextGenerator geminiGenerator,
        IAiTextGenerator deepSeekGenerator,
        IAiTextGenerator openAiGenerator,
        IAiTextGenerator? cliGenerator,
        IOptions<AiOptions> aiOptions,
        ILogger<RoutingTextGenerator>? logger,
        bool isTest)
    {
        this.ollamaGenerator = ollamaGenerator ?? throw new ArgumentNullException(nameof(ollamaGenerator));
        this.geminiGenerator = geminiGenerator ?? throw new ArgumentNullException(nameof(geminiGenerator));
        this.deepSeekGenerator = deepSeekGenerator ?? throw new ArgumentNullException(nameof(deepSeekGenerator));
        this.openAiGenerator = openAiGenerator ?? throw new ArgumentNullException(nameof(openAiGenerator));
        this.cliGenerator = cliGenerator;
        this.aiOptions = aiOptions ?? throw new ArgumentNullException(nameof(aiOptions));
        this.logger = logger;
    }

    public Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetGenerator = ResolveGenerator(request.Provider);
        return targetGenerator.GenerateAsync(request, cancellationToken);
    }

    private IAiTextGenerator ResolveGenerator(string? provider)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            var normalized = provider.Trim().ToLowerInvariant();
            return normalized switch
            {
                "gemini" or "gemini-text" => geminiGenerator,
                "deepseek" => deepSeekGenerator,
                "openai" or "chatgpt" => openAiGenerator,
                "ollama" or "local" => ollamaGenerator,
                "cli" or "gemini-cli" => cliGenerator ?? throw new AiGenerationException(
                    AiErrorCode.ProviderUnavailable,
                    "CLI AI text provider is not registered."),
                _ => throw new AiGenerationException(
                    AiErrorCode.InvalidRequest,
                    $"Unknown AI text provider '{provider}'.")
            };
        }

        var defaultProvider = aiOptions.Value.Provider;
        if (string.Equals(defaultProvider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return geminiGenerator;
        }

        if (string.Equals(defaultProvider, "DeepSeek", StringComparison.OrdinalIgnoreCase))
        {
            return deepSeekGenerator;
        }

        if (string.Equals(defaultProvider, "OpenAi", StringComparison.OrdinalIgnoreCase))
        {
            return openAiGenerator;
        }

        if (string.Equals(defaultProvider, "Cli", StringComparison.OrdinalIgnoreCase)
            || string.Equals(defaultProvider, "GeminiCli", StringComparison.OrdinalIgnoreCase))
        {
            return cliGenerator ?? ollamaGenerator;
        }

        return ollamaGenerator;
    }
}
