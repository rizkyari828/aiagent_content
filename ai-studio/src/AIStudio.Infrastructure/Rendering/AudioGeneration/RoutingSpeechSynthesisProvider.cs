using AIStudio.Application.Rendering.AudioGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering.AudioGeneration;

public sealed class RoutingSpeechSynthesisProvider : ISpeechSynthesisProvider
{
    private readonly ISpeechSynthesisProvider localProvider;
    private readonly ISpeechSynthesisProvider geminiProvider;
    private readonly IOptions<SpeechSynthesisOptions> localOptions;
    private readonly IOptions<GeminiSpeechOptions> geminiOptions;
    private readonly ILogger<RoutingSpeechSynthesisProvider>? logger;

    public RoutingSpeechSynthesisProvider(
        VoxCpmSpeechSynthesisProvider localProvider,
        GeminiSpeechSynthesisProvider geminiProvider,
        IOptions<SpeechSynthesisOptions> localOptions,
        IOptions<GeminiSpeechOptions> geminiOptions,
        ILogger<RoutingSpeechSynthesisProvider>? logger = null)
    {
        this.localProvider = localProvider ?? throw new ArgumentNullException(nameof(localProvider));
        this.geminiProvider = geminiProvider ?? throw new ArgumentNullException(nameof(geminiProvider));
        this.localOptions = localOptions ?? throw new ArgumentNullException(nameof(localOptions));
        this.geminiOptions = geminiOptions ?? throw new ArgumentNullException(nameof(geminiOptions));
        this.logger = logger;
    }

    public static RoutingSpeechSynthesisProvider CreateForTesting(
        ISpeechSynthesisProvider localProvider,
        ISpeechSynthesisProvider geminiProvider,
        IOptions<SpeechSynthesisOptions> localOptions,
        IOptions<GeminiSpeechOptions> geminiOptions,
        ILogger<RoutingSpeechSynthesisProvider>? logger = null) =>
        new(localProvider, geminiProvider, localOptions, geminiOptions, logger, isTest: true);

    private RoutingSpeechSynthesisProvider(
        ISpeechSynthesisProvider localProvider,
        ISpeechSynthesisProvider geminiProvider,
        IOptions<SpeechSynthesisOptions> localOptions,
        IOptions<GeminiSpeechOptions> geminiOptions,
        ILogger<RoutingSpeechSynthesisProvider>? logger,
        bool isTest)
    {
        this.localProvider = localProvider ?? throw new ArgumentNullException(nameof(localProvider));
        this.geminiProvider = geminiProvider ?? throw new ArgumentNullException(nameof(geminiProvider));
        this.localOptions = localOptions ?? throw new ArgumentNullException(nameof(localOptions));
        this.geminiOptions = geminiOptions ?? throw new ArgumentNullException(nameof(geminiOptions));
        this.logger = logger;
    }

    public bool IsEnabled => ResolveDefaultProvider().IsEnabled;

    public Task<SpeechSynthesisResult> SynthesizeAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetProvider = ResolveProvider(request.Provider);
        return targetProvider.SynthesizeAsync(request, cancellationToken);
    }

    private ISpeechSynthesisProvider ResolveProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return ResolveDefaultProvider();
        }

        var normalized = provider.Trim().ToLowerInvariant();
        return normalized switch
        {
            "voxcpm2" or "voxcpm" or "local" => localProvider,
            "gemini-tts" or "gemini" => geminiProvider,
            _ => throw new SpeechSynthesisException(
                "speech_provider_unknown",
                $"Unknown speech synthesis provider '{provider}'.")
        };
    }

    private ISpeechSynthesisProvider ResolveDefaultProvider()
    {
        var local = localOptions.Value;
        var gemini = geminiOptions.Value;
        return !local.Enabled && gemini.Enabled && !string.IsNullOrWhiteSpace(gemini.ApiKey)
            ? geminiProvider
            : localProvider;
    }
}
