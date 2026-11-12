using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.AI;
using AIStudio.Application.Approvals;
using AIStudio.Application.Assets;
using AIStudio.Application.Bibles;
using AIStudio.Application.Capabilities;
using AIStudio.Application.Concepts;
using AIStudio.Application.Content;
using AIStudio.Application.Creative;
using AIStudio.Application.Jobs;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.GenerateAudio;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Narration;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Application.Scripts;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Application.Subtitles;
using AIStudio.Application.Tracking;
using AIStudio.Application.Transfer;
using AIStudio.Infrastructure.AI;
using AIStudio.Infrastructure.AI.Gemini;
using AIStudio.Infrastructure.AI.OpenAiCompatible;
using AIStudio.Infrastructure.Approvals;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Content;
using AIStudio.Infrastructure.Gpu;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Narration;
using AIStudio.Infrastructure.Persistence;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Infrastructure.Rendering.AudioGeneration;
using AIStudio.Infrastructure.Rendering.AudioMixing;
using AIStudio.Infrastructure.Rendering.AudioProduction;
using AIStudio.Infrastructure.Rendering.Gemini;
using AIStudio.Infrastructure.Scripts;
using AIStudio.Infrastructure.Subtitles;
using AIStudio.Infrastructure.Tracking;
using AIStudio.Infrastructure.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AIStudio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        ValidateConnectionString(connectionString);

        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IApplicationDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());

        AddJobWorker(services, configuration);
        AddGpuResourceGate(services, configuration);
        AddAiGateway(services, configuration);
        AddAssetStorage(services, configuration);
        AddRendering(services, configuration);
        AddAudioGeneration(services, configuration);
        AddAudioProduction(services);
        AddCapabilityRegistry(services);
        AddProductionRecipes(services);
        AddConceptRegistry(services);
        AddCreativeDirection(services);
        AddStoryDirector(services, configuration);
        AddBibles(services);
        AddStoryContext(services);
        AddProjectTransfer(services, configuration);

        return services;
    }

    private static void AddGpuResourceGate(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // One shared singleton so every heavy-GPU provider serializes on the same
        // in-process permit.
        services
            .AddOptions<GpuResourceGateOptions>()
            .Bind(configuration.GetSection(GpuResourceGateOptions.SectionName));

        services.AddSingleton<IGpuResourceGate, GpuResourceGate>();
    }

    private static void AddJobWorker(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<JobWorkerOptions>()
            .Bind(configuration.GetSection(JobWorkerOptions.SectionName))
            .Validate(
                options => options.PollInterval > TimeSpan.Zero,
                "JobWorker:PollInterval must be greater than zero.")
            .Validate(
                options => options.LeaseDuration >= TimeSpan.FromSeconds(3),
                "JobWorker:LeaseDuration must be at least three seconds.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IJobQueue, PostgreSqlJobQueue>();
        services.AddScoped<IContentProjectReader, ContentProjectReader>();
        services.AddScoped<IContentProjectRepository, ContentProjectRepository>();
        services.AddScoped<IJobReader, JobReader>();
        services.AddScoped<IProjectJobReader, ProjectJobReader>();
        services.AddScoped<IScriptReviewRepository, ScriptReviewRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IFinalApprovalRepository, FinalApprovalRepository>();
        services.AddScoped<IPublicationRepository, PublicationRepository>();
        services.AddScoped<ITrackingRepository, TrackingRepository>();
        services.AddScoped<IPublicationReferenceLookup, PublicationReferenceLookup>();
        services.AddScoped<INarrationRepository, NarrationRepository>();
        services.AddScoped<ISubtitleRepository, SubtitleRepository>();
        services.AddSingleton<LocalAssetFileStore>();
        services.AddSingleton<IAssetFileStore>(
            serviceProvider => serviceProvider.GetRequiredService<LocalAssetFileStore>());
        services.AddSingleton<IIdentityAssetStore, LocalIdentityAssetStore>();
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IMediaInspector, FfprobeMediaInspector>();
        services.AddSingleton<IVideoRenderer, FfmpegVideoRenderer>();
        services.AddSingleton<IAudioMixer, FfmpegAudioMixer>();
        services.AddSingleton<ISceneVisualRenderer, FfmpegSceneVisualRenderer>();
        services.AddSingleton<IManimSceneRenderer, ProcessManimSceneRenderer>();
        services.AddScoped<IJobHandler, GenerateIdeaJobHandler>();
        services.AddScoped<IJobHandler, GenerateScriptJobHandler>();
        services.AddScoped<IJobHandler, GenerateStoryboardJobHandler>();
        services.AddScoped<IJobHandler, RenderVideoJobHandler>();
        services.AddScoped<IJobHandler, FinalVideoQaJobHandler>();
        services.AddScoped<IJobHandler, GenerateSceneVisualsJobHandler>();
        services.AddScoped<GenerateIdeaWorkflow>();
        services.AddScoped<GenerateScriptWorkflow>();
        services.AddScoped<GenerateStoryboardWorkflow>();
        services.AddScoped<ScriptReviewWorkflow>();
        services.AddScoped<ContentProjectWorkflow>();
        services.AddScoped<ContentReadinessWorkflow>();
        services.AddScoped<CancelJobWorkflow>();
        services.AddScoped<FinalApprovalWorkflow>();
        services.AddScoped<PublicationWorkflow>();
        services.AddScoped<MetricSnapshotWorkflow>();
        services.AddScoped<TimeEntryWorkflow>();
        services.AddScoped<CostEntryWorkflow>();
        services.AddScoped<TrackingSummaryWorkflow>();
        services.AddScoped<AssetCollectionWorkflow>();
        services.AddScoped<NarrationWorkflow>();
        services.AddScoped<SubtitleWorkflow>();
        services.AddScoped<RenderVideoWorkflow>();
        services.AddScoped<FinalVideoQaWorkflow>();
        services.AddScoped<GenerateSceneVisualsWorkflow>();
        services.AddSingleton<IJobHandler, PlaceholderJobHandler>();
        services.AddScoped<JobProcessor>();
        services.AddHostedService<JobWorker>();
    }

    private static void AddAiGateway(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .Validate(
                options => string.Equals(
                        options.Provider,
                        "Ollama",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        options.Provider,
                        "Gemini",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        options.Provider,
                        "DeepSeek",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        options.Provider,
                        "OpenAi",
                        StringComparison.OrdinalIgnoreCase),
                "Ai:Provider must be 'Ollama', 'Gemini', 'DeepSeek', or 'OpenAi'.")
            .ValidateOnStart();

        services
            .AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "Ollama:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DefaultModel),
                "Ollama:DefaultModel is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 600,
                "Ollama:TimeoutSeconds must be between 1 and 600.")
            .ValidateOnStart();

        services
            .AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "Gemini:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DefaultModel),
                "Gemini:DefaultModel is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 600,
                "Gemini:TimeoutSeconds must be between 1 and 600.")
            .ValidateOnStart();

        services
            .AddOptions<DeepSeekOptions>()
            .Bind(configuration.GetSection(DeepSeekOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "DeepSeek:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DefaultModel),
                "DeepSeek:DefaultModel is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 600,
                "DeepSeek:TimeoutSeconds must be between 1 and 600.")
            .ValidateOnStart();

        services
            .AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "OpenAi:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DefaultModel),
                "OpenAi:DefaultModel is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 600,
                "OpenAi:TimeoutSeconds must be between 1 and 600.")
            .ValidateOnStart();

        services.AddHttpClient<OllamaTextGenerator>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<OllamaOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        services.AddHttpClient<GeminiTextGenerator>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<GeminiOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        services.AddHttpClient<DeepSeekTextGenerator>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<DeepSeekOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        services.AddHttpClient<OpenAiTextGenerator>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<OpenAiOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        services.AddTransient<RoutingTextGenerator>();
        services.AddTransient<IAiTextGenerator>(serviceProvider =>
            serviceProvider.GetRequiredService<RoutingTextGenerator>());
    }

    private static void AddProjectTransfer(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ProjectTransferOptions>()
            .Bind(configuration.GetSection(ProjectTransferOptions.SectionName))
            .Validate(
                options => ProjectPackagePath.TryNormalize(options.RootPath, out _),
                "ProjectTransfer:RootPath must be a safe path relative to the asset root.")
            .Validate(
                options => options.MaxFileBytes > 0,
                "ProjectTransfer:MaxFileBytes must be positive.")
            .Validate(
                options => options.MaxPackageBytes >= options.MaxFileBytes,
                "ProjectTransfer:MaxPackageBytes cannot be smaller than MaxFileBytes.")
            .Validate(
                options => options.MaxManifestBytes > 0
                    && options.MaxManifestBytes <= options.MaxPackageBytes,
                "ProjectTransfer:MaxManifestBytes must be positive and not exceed MaxPackageBytes.")
            .Validate(
                options => options.MaxFileCount > 0,
                "ProjectTransfer:MaxFileCount must be positive.")
            .ValidateOnStart();

        services.AddSingleton<IProjectTransferStore, LocalProjectTransferStore>();
        services.AddScoped<IProjectTransferRepository, ProjectTransferRepository>();
        services.AddScoped<ProjectExportWorkflow>();
        services.AddScoped<ProjectImportWorkflow>();
    }

    private static void AddAssetStorage(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AssetStorageOptions>()
            .Bind(configuration.GetSection(AssetStorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.RootPath),
                "Assets:RootPath is required.")
            .ValidateOnStart();
    }

    private static void AddRendering(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RenderingOptions>()
            .Bind(configuration.GetSection(RenderingOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.FfmpegPath),
                "Rendering:FfmpegPath is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.FfprobePath),
                "Rendering:FfprobePath is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 3600,
                "Rendering:TimeoutSeconds must be between 1 and 3600.")
            .Validate(
                options => options.Width is >= 2 and <= 7680,
                "Rendering:Width must be between 2 and 7680.")
            .Validate(
                options => options.Height is >= 2 and <= 4320,
                "Rendering:Height must be between 2 and 4320.")
            .Validate(
                options => options.FrameRate is >= 1 and <= 120,
                "Rendering:FrameRate must be between 1 and 120.")
            .ValidateOnStart();

        services
            .AddOptions<AudioMixingOptions>()
            .Bind(configuration.GetSection(AudioMixingOptions.SectionName))
            .Validate(
                options => options.SampleRate is >= 8000 and <= 192000,
                "AudioMixing:SampleRate must be between 8000 and 192000.")
            .Validate(
                options => options.Channels is 1 or 2,
                "AudioMixing:Channels must be 1 or 2.")
            .Validate(
                options => options.FadeInSeconds >= 0 && options.FadeOutSeconds >= 0,
                "AudioMixing fade durations must not be negative.")
            .Validate(
                options => options.LimiterCeilingDb is <= 0 and >= -20,
                "AudioMixing:LimiterCeilingDb must be between -20 and 0.")
            .ValidateOnStart();

        services
            .AddOptions<ManimOptions>()
            .Bind(configuration.GetSection(ManimOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.PythonPath),
                "Manim:PythonPath is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ScriptPath),
                "Manim:ScriptPath is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 3600,
                "Manim:TimeoutSeconds must be between 1 and 3600.")
            .ValidateOnStart();

        services
            .AddOptions<BlenderOptions>()
            .Bind(configuration.GetSection(BlenderOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ExecutablePath),
                "Blender:ExecutablePath is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.TemplateDirectory),
                "Blender:TemplateDirectory is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 7200,
                "Blender:TimeoutSeconds must be between 1 and 7200.")
            .Validate(
                options => options.Samples is >= 1 and <= 4096,
                "Blender:Samples must be between 1 and 4096.")
            .Validate(
                options => options.Width is >= 16 and <= 7680 && options.Width % 2 == 0,
                "Blender:Width must be an even number between 16 and 7680.")
            .Validate(
                options => options.Height is >= 16 and <= 4320 && options.Height % 2 == 0,
                "Blender:Height must be an even number between 16 and 4320.")
            .Validate(
                options => options.FramesPerSecond is >= 1 and <= 120,
                "Blender:FramesPerSecond must be between 1 and 120.")
            .ValidateOnStart();

        services.AddSingleton<IThreeDRenderingProvider, BlenderThreeDRenderingProvider>();

        services
            .AddOptions<ComfyUiOptions>()
            .Bind(configuration.GetSection(ComfyUiOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "ComfyUi:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.WorkflowPath),
                "ComfyUi:WorkflowPath is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ImageEditWorkflowPath),
                "ComfyUi:ImageEditWorkflowPath is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 3600,
                "ComfyUi:TimeoutSeconds must be between 1 and 3600.")
            .Validate(
                options => options.Width is >= 16 and <= 16384,
                "ComfyUi:Width must be between 16 and 16384.")
            .Validate(
                options => options.Height is >= 16 and <= 16384,
                "ComfyUi:Height must be between 16 and 16384.")
            .ValidateOnStart();

        services
            .AddOptions<GeminiImageOptions>()
            .Bind(configuration.GetSection(GeminiImageOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "GeminiImage:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Model),
                "GeminiImage:Model is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 3600,
                "GeminiImage:TimeoutSeconds must be between 1 and 3600.")
            .ValidateOnStart();

        services.AddHttpClient<ComfyUiImageGenerationProvider>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<ComfyUiOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        services.AddHttpClient<GeminiImageGenerationProvider>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<GeminiImageOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        services.AddTransient<IImageGenerationProvider>(serviceProvider =>
        {
            var comfy = serviceProvider.GetRequiredService<IOptions<ComfyUiOptions>>().Value;
            var gemini = serviceProvider.GetRequiredService<IOptions<GeminiImageOptions>>().Value;
            return !comfy.Enabled && gemini.Enabled && !string.IsNullOrWhiteSpace(gemini.ApiKey)
                ? serviceProvider.GetRequiredService<GeminiImageGenerationProvider>()
                : serviceProvider.GetRequiredService<ComfyUiImageGenerationProvider>();
        });

        services
            .AddOptions<ComfyUiVideoOptions>()
            .Bind(configuration.GetSection(ComfyUiVideoOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "ComfyUiVideo:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.WorkflowPath),
                "ComfyUiVideo:WorkflowPath is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.FirstFrameWorkflowPath),
                "ComfyUiVideo:FirstFrameWorkflowPath is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.OutputDirectory),
                "ComfyUiVideo:OutputDirectory is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 7200,
                "ComfyUiVideo:TimeoutSeconds must be between 1 and 7200.")
            .Validate(
                options => options.MaxSeconds is >= 1 and <= 60,
                "ComfyUiVideo:MaxSeconds must be between 1 and 60.")
            .Validate(
                options => options.Width is >= 16 and <= 4096,
                "ComfyUiVideo:Width must be between 16 and 4096.")
            .Validate(
                options => options.Height is >= 16 and <= 4096,
                "ComfyUiVideo:Height must be between 16 and 4096.")
            .Validate(
                options => options.Fps is >= 1 and <= 120,
                "ComfyUiVideo:Fps must be between 1 and 120.")
            .ValidateOnStart();

        services
            .AddOptions<GeminiVideoOptions>()
            .Bind(configuration.GetSection(GeminiVideoOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "GeminiVideo:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Model),
                "GeminiVideo:Model is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.OutputDirectory),
                "GeminiVideo:OutputDirectory is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 7200,
                "GeminiVideo:TimeoutSeconds must be between 1 and 7200.")
            .Validate(
                options => options.MaxSeconds is >= 1 and <= 60,
                "GeminiVideo:MaxSeconds must be between 1 and 60.")
            .Validate(
                options => options.Width is >= 16 and <= 4096,
                "GeminiVideo:Width must be between 16 and 4096.")
            .Validate(
                options => options.Height is >= 16 and <= 4096,
                "GeminiVideo:Height must be between 16 and 4096.")
            .Validate(
                options => options.Fps is >= 1 and <= 120,
                "GeminiVideo:Fps must be between 1 and 120.")
            .ValidateOnStart();

        // Registered but disabled by default and not selected by any production path
        // in v1; a missing external ComfyUI runtime never affects existing engines.
        services.AddHttpClient<ComfyUiVideoGenerationProvider>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<ComfyUiVideoOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        services.AddHttpClient<GeminiVideoGenerationProvider>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<GeminiVideoOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });

        services.AddTransient<IVideoGenerationProvider>(serviceProvider =>
        {
            var comfy = serviceProvider.GetRequiredService<IOptions<ComfyUiVideoOptions>>().Value;
            var gemini = serviceProvider.GetRequiredService<IOptions<GeminiVideoOptions>>().Value;
            return !comfy.Enabled && gemini.Enabled && !string.IsNullOrWhiteSpace(gemini.ApiKey)
                ? serviceProvider.GetRequiredService<GeminiVideoGenerationProvider>()
                : serviceProvider.GetRequiredService<ComfyUiVideoGenerationProvider>();
        });
    }

    private static void AddAudioGeneration(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<SpeechSynthesisOptions>()
            .Bind(configuration.GetSection(SpeechSynthesisOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.VoxCpm2.PythonExecutable),
                "SpeechSynthesis:VoxCPM2:PythonExecutable is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.VoxCpm2.ScriptPath),
                "SpeechSynthesis:VoxCPM2:ScriptPath is required.")
            .Validate(
                options => options.VoxCpm2.TimeoutSeconds is >= 1 and <= 3600,
                "SpeechSynthesis:VoxCPM2:TimeoutSeconds must be between 1 and 3600.")
            .Validate(
                options => options.VoxCpm2.InferenceTimesteps is >= 1 and <= 100,
                "SpeechSynthesis:VoxCPM2:InferenceTimesteps must be between 1 and 100.")
            .Validate(
                options => options.VoxCpm2.ExpectedSampleRate is >= 8000 and <= 192000,
                "SpeechSynthesis:VoxCPM2:ExpectedSampleRate must be between 8000 and 192000.")
            .ValidateOnStart();

        services
            .AddOptions<GeminiSpeechOptions>()
            .Bind(configuration.GetSection(GeminiSpeechOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "GeminiSpeech:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Model),
                "GeminiSpeech:Model is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.FfmpegPath),
                "GeminiSpeech:FfmpegPath is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 3600,
                "GeminiSpeech:TimeoutSeconds must be between 1 and 3600.")
            .Validate(
                options => options.ExpectedSampleRate is >= 8000 and <= 192000,
                "GeminiSpeech:ExpectedSampleRate must be between 8000 and 192000.")
            .ValidateOnStart();

        services
            .AddOptions<MusicGenerationOptions>()
            .Bind(configuration.GetSection(MusicGenerationOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AceStep.PythonExecutable),
                "MusicGeneration:AceStep:PythonExecutable is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AceStep.ScriptPath),
                "MusicGeneration:AceStep:ScriptPath is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AceStep.Model),
                "MusicGeneration:AceStep:Model is required.")
            .Validate(
                options => options.AceStep.TimeoutSeconds is >= 1 and <= 7200,
                "MusicGeneration:AceStep:TimeoutSeconds must be between 1 and 7200.")
            .Validate(
                options => options.AceStep.InferenceSteps is >= 1 and <= 100,
                "MusicGeneration:AceStep:InferenceSteps must be between 1 and 100.")
            .Validate(
                options => options.AceStep.ExpectedSampleRate is >= 8000 and <= 192000,
                "MusicGeneration:AceStep:ExpectedSampleRate must be between 8000 and 192000.")
            .Validate(
                options => options.AceStep.ExpectedChannels is 1 or 2,
                "MusicGeneration:AceStep:ExpectedChannels must be 1 or 2.")
            .ValidateOnStart();

        services.AddSingleton<VoxCpmSpeechSynthesisProvider>();
        services.AddHttpClient<GeminiSpeechSynthesisProvider>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<GeminiSpeechOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });
        services.AddTransient<ISpeechSynthesisProvider>(serviceProvider =>
        {
            var localSpeech = serviceProvider.GetRequiredService<IOptions<SpeechSynthesisOptions>>().Value;
            var geminiSpeech = serviceProvider.GetRequiredService<IOptions<GeminiSpeechOptions>>().Value;
            return !localSpeech.Enabled && geminiSpeech.Enabled && !string.IsNullOrWhiteSpace(geminiSpeech.ApiKey)
                ? serviceProvider.GetRequiredService<GeminiSpeechSynthesisProvider>()
                : serviceProvider.GetRequiredService<VoxCpmSpeechSynthesisProvider>();
        });
        services.AddSingleton<IMusicGenerationProvider, AceStepMusicGenerationProvider>();
    }

    private static void AddAudioProduction(IServiceCollection services)
    {
        // Deterministic workspace layout + narrow reuse manifest shared by the
        // GenerateAudio handler and the renderer.
        services.AddSingleton<AudioProductionWorkspace>();
        services.AddSingleton<IAudioNarrationAssembler, FfmpegNarrationAssembler>();
        services.AddScoped<IJobHandler, GenerateAudioJobHandler>();
        services.AddScoped<GenerateAudioWorkflow>();
    }

    private static void AddCapabilityRegistry(IServiceCollection services)
    {
        // Metadata/config only: the factory reads the existing enablement options
        // and builds the trusted catalog. It never creates or contacts a provider,
        // so capability discovery works even when no external runtime is installed.
        services.AddSingleton<ICapabilityRegistry>(serviceProvider =>
        {
            var ai = serviceProvider
                .GetRequiredService<IOptions<AiOptions>>()
                .Value;
            var geminiText = serviceProvider
                .GetRequiredService<IOptions<GeminiOptions>>()
                .Value;
            var deepseek = serviceProvider
                .GetRequiredService<IOptions<DeepSeekOptions>>()
                .Value;
            var openAi = serviceProvider
                .GetRequiredService<IOptions<OpenAiOptions>>()
                .Value;
            var speech = serviceProvider
                .GetRequiredService<IOptions<SpeechSynthesisOptions>>()
                .Value;
            var geminiSpeech = serviceProvider
                .GetRequiredService<IOptions<GeminiSpeechOptions>>()
                .Value;
            var music = serviceProvider
                .GetRequiredService<IOptions<MusicGenerationOptions>>()
                .Value;
            var manim = serviceProvider
                .GetRequiredService<IOptions<ManimOptions>>()
                .Value;
            var images = serviceProvider
                .GetRequiredService<IOptions<ComfyUiOptions>>()
                .Value;
            var geminiImages = serviceProvider
                .GetRequiredService<IOptions<GeminiImageOptions>>()
                .Value;
            var threeD = serviceProvider
                .GetRequiredService<IOptions<BlenderOptions>>()
                .Value;
            var video = serviceProvider
                .GetRequiredService<IOptions<ComfyUiVideoOptions>>()
                .Value;
            var geminiVideo = serviceProvider
                .GetRequiredService<IOptions<GeminiVideoOptions>>()
                .Value;

            var providers = ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: speech.Enabled,
                musicEnabled: music.Enabled,
                manimEnabled: manim.Enabled,
                imageEnabled: images.Enabled,
                threeDEnabled: threeD.Enabled,
                videoEnabled: video.Enabled,
                textEnabled: string.Equals(ai.Provider, "Ollama", StringComparison.OrdinalIgnoreCase),
                geminiTextEnabled: geminiText.Enabled && !string.IsNullOrWhiteSpace(geminiText.ApiKey),
                geminiImageEnabled: geminiImages.Enabled && !string.IsNullOrWhiteSpace(geminiImages.ApiKey),
                geminiVideoEnabled: geminiVideo.Enabled && !string.IsNullOrWhiteSpace(geminiVideo.ApiKey),
                geminiSpeechEnabled: geminiSpeech.Enabled && !string.IsNullOrWhiteSpace(geminiSpeech.ApiKey),
                deepseekEnabled: deepseek.Enabled && !string.IsNullOrWhiteSpace(deepseek.ApiKey),
                openAiEnabled: openAi.Enabled && !string.IsNullOrWhiteSpace(openAi.ApiKey));

            return new CapabilityRegistry(
                ProductionCapabilityCatalog.Capabilities,
                providers);
        });
    }

    private static void AddProductionRecipes(IServiceCollection services)
    {
        // Recipes are declarative data. The registry is seeded from the small
        // trusted catalog and resolution delegates to the capability registry;
        // neither touches a provider or the database.
        services.AddSingleton<IProductionRecipeRegistry>(
            _ => new ProductionRecipeRegistry(SeedProductionRecipes.All));
        services.AddSingleton<IProductionRecipeResolver, ProductionRecipeResolver>();
    }

    private static void AddConceptRegistry(IServiceCollection services)
    {
        // Concepts are declarative data referencing trusted recipes. The registry
        // is seeded from the small trusted catalog and resolution delegates to the
        // recipe resolver; no provider or database is touched.
        services.AddSingleton<IConceptRegistry>(_ => new ConceptRegistry(SeedConcepts.All));
        services.AddSingleton<IConceptResolver, ConceptResolver>();
    }

    private static void AddCreativeDirection(IServiceCollection services)
    {
        // The Creative Director consumes approved ideas and returns a proposed
        // direction. The planning context is a read-only projection of the trusted
        // registries; the director itself only talks to the existing AI boundary.
        services.AddSingleton<ICreativePlanningContextProvider, CreativePlanningContextProvider>();
        services.AddScoped<ICreativeDirector, CreativeDirector>();
    }

    private static void AddStoryDirector(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Narrative patterns are declarative data seeded from the small trusted
        // catalog. The deterministic director stays the default so production
        // behavior is unchanged; the Qwen-backed director is an explicit opt-in via
        // StoryDirector:Mode=qwen. Neither mode falls back to the other.
        services
            .AddOptions<StoryDirectorOptions>()
            .Bind(configuration.GetSection(StoryDirectorOptions.SectionName))
            .Validate(
                options => StoryDirectorOptions.IsKnownMode(options.Mode),
                $"StoryDirector:Mode must be '{StoryDirectorOptions.DeterministicMode}' or '{StoryDirectorOptions.QwenMode}'.")
            .ValidateOnStart();

        services.AddSingleton<INarrativePatternRegistry>(
            _ => new NarrativePatternRegistry(SeedNarrativePatterns.All));

        services.AddSingleton<IStoryDirector>(serviceProvider =>
        {
            var mode = serviceProvider
                .GetRequiredService<IOptions<StoryDirectorOptions>>()
                .Value
                .Mode;

            if (string.Equals(mode, StoryDirectorOptions.DeterministicMode, StringComparison.OrdinalIgnoreCase))
            {
                return new StoryDirector(
                    serviceProvider.GetRequiredService<INarrativePatternRegistry>());
            }

            if (string.Equals(mode, StoryDirectorOptions.QwenMode, StringComparison.OrdinalIgnoreCase))
            {
                return new QwenStoryDirector(
                    serviceProvider.GetRequiredService<IAiTextGenerator>(),
                    serviceProvider.GetRequiredService<INarrativePatternRegistry>());
            }

            throw new InvalidOperationException(
                $"StoryDirector:Mode '{mode}' is invalid. Use '{StoryDirectorOptions.DeterministicMode}' or '{StoryDirectorOptions.QwenMode}'.");
        });
    }

    private static void AddBibles(IServiceCollection services)
    {
        // Character/world bibles are declarative data held in trusted in-memory
        // registries. v1 seeds nothing: identities arrive later as data (or from a
        // future planner), never as code. No database, asset storage, or provider
        // is wired here.
        services.AddSingleton<ICharacterBibleRegistry>(_ => new CharacterBibleRegistry());
        services.AddSingleton<IWorldBibleRegistry>(_ => new WorldBibleRegistry());
        services.AddSingleton<IIdentityAssetMetadataPersistence, LocalIdentityAssetMetadataPersistence>();
        services.AddSingleton<IIdentityAssetRegistry>(serviceProvider =>
            new IdentityAssetRegistry(
                timeProvider: serviceProvider.GetRequiredService<TimeProvider>(),
                persistence: serviceProvider.GetRequiredService<IIdentityAssetMetadataPersistence>()));
        services.AddSingleton<IIdentityAssetResolver, IdentityAssetResolver>();
        services.AddScoped<ImportIdentityAssetWorkflow>();
        services.AddScoped<ApproveIdentityAssetWorkflow>();
        services.AddSingleton<StoryPlanGrounder>();
        services.AddScoped<IStoryBiblePlanner, QwenStoryBiblePlanner>();
    }

    private static void AddStoryContext(IServiceCollection services)
    {
        // The context builder is a pure, AI-free projector over the trusted
        // registries. It executes nothing and touches no provider or database.
        services.AddSingleton<IStoryContextBuilder, StoryContextBuilder>();
    }

    private static bool IsValidBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static void ValidateConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is required. " +
                "Set ConnectionStrings__DefaultConnection in the environment.");
        }

        NpgsqlConnectionStringBuilder parsed;

        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is invalid.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(parsed.Host)
            || string.IsNullOrWhiteSpace(parsed.Database)
            || string.IsNullOrWhiteSpace(parsed.Username))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' must include Host, Database, and Username.");
        }
    }
}
