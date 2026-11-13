using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Rendering.Gemini;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

public sealed class RoutingVideoGenerationProvider : IVideoGenerationProvider
{
    private readonly IVideoGenerationProvider comfyProvider;
    private readonly IVideoGenerationProvider geminiProvider;
    private readonly IOptions<ComfyUiVideoOptions> comfyOptions;
    private readonly IOptions<GeminiVideoOptions> geminiOptions;
    private readonly ILogger<RoutingVideoGenerationProvider>? logger;

    public RoutingVideoGenerationProvider(
        ComfyUiVideoGenerationProvider comfyProvider,
        GeminiVideoGenerationProvider geminiProvider,
        IOptions<ComfyUiVideoOptions> comfyOptions,
        IOptions<GeminiVideoOptions> geminiOptions,
        ILogger<RoutingVideoGenerationProvider>? logger = null)
    {
        this.comfyProvider = comfyProvider ?? throw new ArgumentNullException(nameof(comfyProvider));
        this.geminiProvider = geminiProvider ?? throw new ArgumentNullException(nameof(geminiProvider));
        this.comfyOptions = comfyOptions ?? throw new ArgumentNullException(nameof(comfyOptions));
        this.geminiOptions = geminiOptions ?? throw new ArgumentNullException(nameof(geminiOptions));
        this.logger = logger;
    }

    public static RoutingVideoGenerationProvider CreateForTesting(
        IVideoGenerationProvider comfyProvider,
        IVideoGenerationProvider geminiProvider,
        IOptions<ComfyUiVideoOptions> comfyOptions,
        IOptions<GeminiVideoOptions> geminiOptions,
        ILogger<RoutingVideoGenerationProvider>? logger = null) =>
        new(comfyProvider, geminiProvider, comfyOptions, geminiOptions, logger, isTest: true);

    private RoutingVideoGenerationProvider(
        IVideoGenerationProvider comfyProvider,
        IVideoGenerationProvider geminiProvider,
        IOptions<ComfyUiVideoOptions> comfyOptions,
        IOptions<GeminiVideoOptions> geminiOptions,
        ILogger<RoutingVideoGenerationProvider>? logger,
        bool isTest)
    {
        this.comfyProvider = comfyProvider ?? throw new ArgumentNullException(nameof(comfyProvider));
        this.geminiProvider = geminiProvider ?? throw new ArgumentNullException(nameof(geminiProvider));
        this.comfyOptions = comfyOptions ?? throw new ArgumentNullException(nameof(comfyOptions));
        this.geminiOptions = geminiOptions ?? throw new ArgumentNullException(nameof(geminiOptions));
        this.logger = logger;
    }

    public bool IsEnabled => ResolveDefaultProvider().IsEnabled;

    public Task<VideoGenerationResult> GenerateAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetProvider = ResolveProvider(request.Provider);
        return targetProvider.GenerateAsync(request, cancellationToken);
    }

    private IVideoGenerationProvider ResolveProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return ResolveDefaultProvider();
        }

        var normalized = provider.Trim().ToLowerInvariant();
        return normalized switch
        {
            "comfyui-i2v" or "comfyui" or "local" => comfyProvider,
            "gemini-veo" or "gemini" or "veo" => geminiProvider,
            _ => throw new VideoGenerationException(
                VideoGenerationException.RequestInvalid,
                $"Unknown video generation provider '{provider}'.")
        };
    }

    private IVideoGenerationProvider ResolveDefaultProvider()
    {
        var comfy = comfyOptions.Value;
        var gemini = geminiOptions.Value;
        return !comfy.Enabled && gemini.Enabled && !string.IsNullOrWhiteSpace(gemini.ApiKey)
            ? geminiProvider
            : comfyProvider;
    }
}
