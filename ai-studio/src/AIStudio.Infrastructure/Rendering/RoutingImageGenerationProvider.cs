using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Rendering.Gemini;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

public sealed class RoutingImageGenerationProvider : IImageGenerationProvider
{
    private readonly IImageGenerationProvider comfyProvider;
    private readonly IImageGenerationProvider geminiProvider;
    private readonly IOptions<ComfyUiOptions> comfyOptions;
    private readonly IOptions<GeminiImageOptions> geminiOptions;
    private readonly ILogger<RoutingImageGenerationProvider>? logger;

    public RoutingImageGenerationProvider(
        ComfyUiImageGenerationProvider comfyProvider,
        GeminiImageGenerationProvider geminiProvider,
        IOptions<ComfyUiOptions> comfyOptions,
        IOptions<GeminiImageOptions> geminiOptions,
        ILogger<RoutingImageGenerationProvider>? logger = null)
    {
        this.comfyProvider = comfyProvider ?? throw new ArgumentNullException(nameof(comfyProvider));
        this.geminiProvider = geminiProvider ?? throw new ArgumentNullException(nameof(geminiProvider));
        this.comfyOptions = comfyOptions ?? throw new ArgumentNullException(nameof(comfyOptions));
        this.geminiOptions = geminiOptions ?? throw new ArgumentNullException(nameof(geminiOptions));
        this.logger = logger;
    }

    public static RoutingImageGenerationProvider CreateForTesting(
        IImageGenerationProvider comfyProvider,
        IImageGenerationProvider geminiProvider,
        IOptions<ComfyUiOptions> comfyOptions,
        IOptions<GeminiImageOptions> geminiOptions,
        ILogger<RoutingImageGenerationProvider>? logger = null) =>
        new(comfyProvider, geminiProvider, comfyOptions, geminiOptions, logger, isTest: true);

    private RoutingImageGenerationProvider(
        IImageGenerationProvider comfyProvider,
        IImageGenerationProvider geminiProvider,
        IOptions<ComfyUiOptions> comfyOptions,
        IOptions<GeminiImageOptions> geminiOptions,
        ILogger<RoutingImageGenerationProvider>? logger,
        bool isTest)
    {
        this.comfyProvider = comfyProvider ?? throw new ArgumentNullException(nameof(comfyProvider));
        this.geminiProvider = geminiProvider ?? throw new ArgumentNullException(nameof(geminiProvider));
        this.comfyOptions = comfyOptions ?? throw new ArgumentNullException(nameof(comfyOptions));
        this.geminiOptions = geminiOptions ?? throw new ArgumentNullException(nameof(geminiOptions));
        this.logger = logger;
    }

    public bool IsEnabled => ResolveDefaultProvider().IsEnabled;

    public Task<byte[]> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetProvider = ResolveProvider(request.Provider);
        return targetProvider.GenerateAsync(request, cancellationToken);
    }

    private IImageGenerationProvider ResolveProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return ResolveDefaultProvider();
        }

        var normalized = provider.Trim().ToLowerInvariant();
        return normalized switch
        {
            "comfyui-flux" or "comfyui" or "local" => comfyProvider,
            "gemini-nanobanana" or "gemini" or "nanobanana" => geminiProvider,
            _ => throw new RenderVideoException(
                "image_provider_unknown",
                $"Unknown image generation provider '{provider}'.")
        };
    }

    private IImageGenerationProvider ResolveDefaultProvider()
    {
        var comfy = comfyOptions.Value;
        var gemini = geminiOptions.Value;
        return !comfy.Enabled && gemini.Enabled && !string.IsNullOrWhiteSpace(gemini.ApiKey)
            ? geminiProvider
            : comfyProvider;
    }
}
