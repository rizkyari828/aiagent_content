using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Gpu;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Infrastructure.Rendering.AudioGeneration;
using AIStudio.Infrastructure.Rendering.AudioMixing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Real, opt-in end-to-end validation against the installed local VoxCPM2 and
/// ACE-Step runtimes. It is skipped unless the machine-specific paths are provided
/// through environment variables, so the default suite never touches GPU runtimes
/// or models:
/// <list type="bullet">
/// <item><c>AISTUDIO_VOXCPM_PYTHON</c> / <c>AISTUDIO_VOXCPM_MODEL</c></item>
/// <item><c>AISTUDIO_ACESTEP_PYTHON</c> / <c>AISTUDIO_ACESTEP_ROOT</c></item>
/// </list>
/// </summary>
public sealed class AudioGenerationEndToEndTests : IDisposable
{
    private readonly string root;
    private readonly SystemProcessRunner runner = new();
    private readonly RenderingOptions rendering = new();

    public AudioGenerationEndToEndTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-audio-generation-e2e",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RealProviders_SynthesizeNarrationAndMusic_ThenMix()
    {
        var voxPython = Environment.GetEnvironmentVariable("AISTUDIO_VOXCPM_PYTHON");
        var voxModel = Environment.GetEnvironmentVariable("AISTUDIO_VOXCPM_MODEL");
        var acePython = Environment.GetEnvironmentVariable("AISTUDIO_ACESTEP_PYTHON");
        var aceRoot = Environment.GetEnvironmentVariable("AISTUDIO_ACESTEP_ROOT");

        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(voxPython)
            && !string.IsNullOrWhiteSpace(voxModel)
            && !string.IsNullOrWhiteSpace(acePython)
            && !string.IsNullOrWhiteSpace(aceRoot),
            "Set AISTUDIO_VOXCPM_PYTHON/MODEL and AISTUDIO_ACESTEP_PYTHON/ROOT to run the real audio generation E2E.");

        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var assetFileStore = new LocalAssetFileStore(storage);
        var renderingOptions = Options.Create(rendering);
        var inspector = new FfprobeMediaInspector(renderingOptions, runner);
        var gate = new GpuResourceGate(
            Options.Create(new GpuResourceGateOptions { Enabled = true }),
            NullLogger<GpuResourceGate>.Instance);

        // --- VoxCPM2 narration -------------------------------------------------
        var speech = new VoxCpmSpeechSynthesisProvider(
            Options.Create(new SpeechSynthesisOptions
            {
                Enabled = true,
                VoxCpm2 = new SpeechSynthesisVoxCpmOptions
                {
                    PythonExecutable = voxPython!,
                    ModelPath = voxModel!,
                    ScriptPath = Script("VoxCPM2", "synthesize.py"),
                    TimeoutSeconds = 600,
                    ExpectedSampleRate = 48000
                }
            }),
            assetFileStore,
            runner,
            inspector,
            gate);

        var narration = await speech.SynthesizeAsync(
            new SpeechSynthesisRequest(
                "Teknologi kecerdasan buatan sekarang bisa berjalan langsung di komputer kita sendiri.",
                SpeechVoiceProfile.Formal,
                "audio/narration/e2e.wav"),
            TestContext.Current.CancellationToken);

        Assert.Equal(48000, narration.SampleRate);
        Assert.True(narration.DurationSeconds > 0, "narration duration must be positive");
        Assert.True(narration.ByteSize > 0, "narration file must not be empty");

        // --- ACE-Step music ----------------------------------------------------
        var music = new AceStepMusicGenerationProvider(
            Options.Create(new MusicGenerationOptions
            {
                Enabled = true,
                AceStep = new MusicGenerationAceStepOptions
                {
                    PythonExecutable = acePython!,
                    ProjectRoot = aceRoot!,
                    Model = "acestep-v15-turbo",
                    ScriptPath = Script("ACE-Step", "generate.py"),
                    TimeoutSeconds = 900,
                    ExpectedSampleRate = 48000,
                    ExpectedChannels = 2
                }
            }),
            assetFileStore,
            runner,
            inspector,
            gate);

        var bed = await music.GenerateAsync(
            new MusicGenerationRequest(
                "instrumental modern ambient electronic background music, warm futuristic technology mood, "
                + "low energy, clean production, subtle synth textures, soft rhythmic pulse, "
                + "suitable under spoken narration, no vocals",
                DurationSeconds: 12,
                Bpm: 90,
                Instrumental: true,
                RelativeOutputPath: "audio/music/e2e.wav",
                Seed: 42),
            TestContext.Current.CancellationToken);

        Assert.Equal(48000, bed.SampleRate);
        Assert.Equal(2, bed.Channels);
        Assert.InRange(bed.DurationSeconds, 9, 15);
        Assert.True(bed.ByteSize > 0, "music file must not be empty");

        // --- Optional final mix through the existing CPU-only mixer ------------
        var mixer = new FfmpegAudioMixer(
            Options.Create(new AudioMixingOptions()),
            renderingOptions,
            storage,
            assetFileStore,
            runner,
            inspector);

        var mix = await mixer.MixAsync(
            new AudioMixRequest("audio/narration/e2e.wav", "audio/music/e2e.wav", "renders/final_mix.wav"),
            TestContext.Current.CancellationToken);

        Assert.Equal(48000, mix.SampleRate);
        Assert.Equal(2, mix.Channels);
        Assert.InRange(
            mix.DurationSeconds,
            narration.DurationSeconds - 1,
            narration.DurationSeconds + 1);
        Assert.True(File.Exists(Path.Combine(root, "renders", "final_mix.wav")));
    }

    private static string Script(string folder, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Rendering", folder, fileName);
}
