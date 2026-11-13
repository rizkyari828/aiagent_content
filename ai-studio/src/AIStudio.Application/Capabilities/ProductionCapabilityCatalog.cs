namespace AIStudio.Application.Capabilities;

/// <summary>
/// Compile-time mapping of the current trusted production stack to capabilities.
/// The provider implementations are trusted application code; only stable ids,
/// priorities, runtime categories and configuration-driven enablement are described
/// here. No executable path, secret, or model path is part of a descriptor, and
/// building the registry never starts an external runtime. A new content concept
/// should request these capability ids as data; it cannot add an implementation.
/// </summary>
public static class ProductionCapabilityCatalog
{
    public static IReadOnlyList<CapabilityDescriptor> Capabilities { get; } =
    [
        new() { Id = CapabilityIds.TextGeneration, DisplayName = "AI text and script generation", Category = "text" },
        new() { Id = CapabilityIds.SpeechNarration, DisplayName = "Speech narration", Category = "speech" },
        new() { Id = CapabilityIds.MusicInstrumental, DisplayName = "Instrumental music", Category = "music" },
        new() { Id = CapabilityIds.VisualStill, DisplayName = "Deterministic still visual", Category = "visual" },
        new() { Id = CapabilityIds.VisualDiagram, DisplayName = "Animated diagram", Category = "visual" },
        new() { Id = CapabilityIds.VisualUiMotion, DisplayName = "UI motion graphics", Category = "visual" },
        new() { Id = CapabilityIds.VisualAiImage, DisplayName = "AI generated image", Category = "visual" },
        new() { Id = CapabilityIds.VisualThreeD, DisplayName = "3D scene render", Category = "visual" },
        new() { Id = CapabilityIds.VisualCharacterAnimation, DisplayName = "Character animation (I2V)", Category = "visual" },
        new() { Id = CapabilityIds.MediaCompose, DisplayName = "Media composition", Category = "media" },
        new() { Id = CapabilityIds.SubtitleBurned, DisplayName = "Burned-in subtitles", Category = "subtitle" }
    ];

    /// <summary>
    /// Builds the provider descriptors for the current stack, applying the
    /// configuration-controlled enablement of the optional GPU providers. Always-on
    /// CPU capabilities stay enabled because they do not depend on an external
    /// runtime being present.
    /// </summary>
    public static IReadOnlyList<CapabilityProviderDescriptor> CreateProviders(
        bool speechEnabled,
        bool musicEnabled,
        bool manimEnabled,
        bool imageEnabled,
        bool threeDEnabled,
        bool videoEnabled,
        bool textEnabled = true,
        bool geminiTextEnabled = false,
        bool geminiImageEnabled = false,
        bool geminiVideoEnabled = false,
        bool geminiSpeechEnabled = false,
        bool deepseekEnabled = false,
        bool openAiEnabled = false,
        bool cliTextEnabled = false) =>
    [
        new()
        {
            ProviderId = "ollama",
            CapabilityId = CapabilityIds.TextGeneration,
            Priority = 100,
            Enabled = textEnabled,
            RuntimeCategory = "local_service_gpu"
        },
        new()
        {
            ProviderId = "gemini-text",
            CapabilityId = CapabilityIds.TextGeneration,
            Priority = 50,
            Enabled = geminiTextEnabled,
            RuntimeCategory = "cloud_api"
        },
        new()
        {
            ProviderId = "deepseek",
            CapabilityId = CapabilityIds.TextGeneration,
            Priority = 45,
            Enabled = deepseekEnabled,
            RuntimeCategory = "cloud_api"
        },
        new()
        {
            ProviderId = "openai",
            CapabilityId = CapabilityIds.TextGeneration,
            Priority = 40,
            Enabled = openAiEnabled,
            RuntimeCategory = "cloud_api"
        },
        new()
        {
            ProviderId = "gemini-cli",
            CapabilityId = CapabilityIds.TextGeneration,
            Priority = 20,
            Enabled = cliTextEnabled,
            RuntimeCategory = "local_cli_experimental"
        },
        new()
        {
            ProviderId = "voxcpm2",
            CapabilityId = CapabilityIds.SpeechNarration,
            Priority = 100,
            Enabled = speechEnabled,
            RuntimeCategory = "local_python_gpu"
        },
        new()
        {
            ProviderId = "gemini-tts",
            CapabilityId = CapabilityIds.SpeechNarration,
            Priority = 50,
            Enabled = geminiSpeechEnabled,
            RuntimeCategory = "cloud_api"
        },
        new()
        {
            ProviderId = "ace-step",
            CapabilityId = CapabilityIds.MusicInstrumental,
            Priority = 100,
            Enabled = musicEnabled,
            RuntimeCategory = "local_python_gpu"
        },
        new()
        {
            ProviderId = "svg-still",
            CapabilityId = CapabilityIds.VisualStill,
            Priority = 100,
            Enabled = true,
            RuntimeCategory = "in_process_cpu"
        },
        new()
        {
            ProviderId = "manim",
            CapabilityId = CapabilityIds.VisualDiagram,
            Priority = 100,
            Enabled = manimEnabled,
            RuntimeCategory = "local_python"
        },
        new()
        {
            ProviderId = "animated-svg",
            CapabilityId = CapabilityIds.VisualUiMotion,
            Priority = 100,
            Enabled = true,
            RuntimeCategory = "in_process_cpu"
        },
        new()
        {
            ProviderId = "comfyui-flux",
            CapabilityId = CapabilityIds.VisualAiImage,
            Priority = 100,
            Enabled = imageEnabled,
            RuntimeCategory = "local_service_gpu"
        },
        new()
        {
            ProviderId = "gemini-nanobanana",
            CapabilityId = CapabilityIds.VisualAiImage,
            Priority = 50,
            Enabled = geminiImageEnabled,
            RuntimeCategory = "cloud_api"
        },
        new()
        {
            ProviderId = "blender",
            CapabilityId = CapabilityIds.VisualThreeD,
            Priority = 100,
            Enabled = threeDEnabled,
            RuntimeCategory = "external_process_gpu"
        },
        new()
        {
            ProviderId = "comfyui-i2v",
            CapabilityId = CapabilityIds.VisualCharacterAnimation,
            Priority = 100,
            Enabled = videoEnabled,
            RuntimeCategory = "local_service_gpu"
        },
        new()
        {
            ProviderId = "gemini-veo",
            CapabilityId = CapabilityIds.VisualCharacterAnimation,
            Priority = 50,
            Enabled = geminiVideoEnabled,
            RuntimeCategory = "cloud_api"
        },
        new()
        {
            ProviderId = "ffmpeg-compose",
            CapabilityId = CapabilityIds.MediaCompose,
            Priority = 100,
            Enabled = true,
            RuntimeCategory = "external_process_cpu"
        },
        new()
        {
            ProviderId = "ffmpeg-subtitle-burn",
            CapabilityId = CapabilityIds.SubtitleBurned,
            Priority = 100,
            Enabled = true,
            RuntimeCategory = "external_process_cpu"
        }
    ];
}
