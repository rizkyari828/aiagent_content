using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Infrastructure.Rendering.AudioGeneration;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class VoxCpmSpeechSynthesisProviderTests : IDisposable
{
    private readonly string root;
    private readonly string assetRoot;
    private readonly string modelDirectory;
    private readonly string scriptFile;

    public VoxCpmSpeechSynthesisProviderTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-speech-tests",
            Guid.NewGuid().ToString("N"));
        assetRoot = Path.Combine(root, "assets");
        modelDirectory = Path.Combine(root, "model");
        scriptFile = Path.Combine(root, "synthesize.py");
        Directory.CreateDirectory(modelDirectory);
        Directory.CreateDirectory(assetRoot);
        File.WriteAllText(scriptFile, "# repository-owned launcher");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SynthesizeAsync_ThrowsWhenDisabled()
    {
        var processRunner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => CreateProvider(processRunner, BuildOptions(enabled: false)).SynthesizeAsync(
                Request(),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_disabled", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task SynthesizeAsync_PersistsAssetAndReportsMetadata()
    {
        var outputBytes = new byte[] { 7, 7, 7 };
        var gate = new RecordingGpuResourceGate();
        string? voice = null;
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(
                audioBytes: outputBytes,
                onRequest: json => voice = json.GetProperty("voice").GetString()),
            gate: gate);

        var result = await provider.SynthesizeAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Equal("audio/narration/clip.wav", result.RelativePath);
        Assert.Equal(RenderVideoTestData.Hash(outputBytes), result.ContentHash);
        Assert.Equal(outputBytes.Length, result.ByteSize);
        Assert.Equal(1.5, result.DurationSeconds);
        Assert.Equal(48000, result.SampleRate);
        Assert.Equal(SpeechVoiceProfile.Formal, result.Voice);
        Assert.True(File.Exists(Path.Combine(assetRoot, "audio", "narration", "clip.wav")));

        Assert.Contains("presenter teknologi", voice);
        Assert.Contains("Bahasa Indonesia alami", voice);

        Assert.Equal(1, gate.AcquireCount);
        Assert.Equal(0, gate.ActiveLeases);
        Assert.Contains("acquire:speech", gate.Events);
        Assert.Contains("release:speech", gate.Events);
    }

    [Theory]
    [InlineData(SpeechVoiceProfile.Formal, "presenter teknologi")]
    [InlineData(SpeechVoiceProfile.Playful, "sedikit jenaka")]
    [InlineData(SpeechVoiceProfile.Energetic, "bersemangat")]
    [InlineData(SpeechVoiceProfile.Documentary, "sinematik")]
    public async Task SynthesizeAsync_MapsApprovedVoiceProfiles(
        SpeechVoiceProfile profile,
        string expected)
    {
        string? voice = null;
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(
                onRequest: json => voice = json.GetProperty("voice").GetString()));

        await provider.SynthesizeAsync(
            new SpeechSynthesisRequest("Halo dunia", profile, "audio/narration/a.wav"),
            TestContext.Current.CancellationToken);

        Assert.Contains(expected, voice);
        Assert.Contains("Bahasa Indonesia alami", voice);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsInvalidText()
    {
        var provider = CreateProvider(new FakeProcessRunner());

        var blank = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest("  ", SpeechVoiceProfile.Formal, "audio/a.wav"),
                TestContext.Current.CancellationToken));
        var tooLong = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest(
                    new string('a', 2001),
                    SpeechVoiceProfile.Formal,
                    "audio/a.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_invalid_input", blank.ErrorCode);
        Assert.Equal("speech_invalid_input", tooLong.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsUnsupportedVoiceAndLocale()
    {
        var provider = CreateProvider(new FakeProcessRunner());

        var voice = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest("Halo", (SpeechVoiceProfile)99, "audio/a.wav"),
                TestContext.Current.CancellationToken));
        var locale = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest("Halo", SpeechVoiceProfile.Formal, "audio/a.wav", "en-US"),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_invalid_input", voice.ErrorCode);
        Assert.Equal("speech_invalid_input", locale.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsOutputPathEscapingAssetRoot()
    {
        var provider = CreateProvider(AudioGenerationTestSupport.Runner());

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest("Halo", SpeechVoiceProfile.Formal, "../escape.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_invalid_input", exception.ErrorCode);
        Assert.False(File.Exists(Path.Combine(root, "escape.wav")));
    }

    [Fact]
    public async Task SynthesizeAsync_HandlesSpacesAndShellMetacharactersSafely()
    {
        const string text = "Halo; rm -rf / \"quote\"\nbaris baru";
        var processRunner = AudioGenerationTestSupport.Runner();
        var provider = CreateProvider(processRunner);

        var result = await provider.SynthesizeAsync(
            new SpeechSynthesisRequest(text, SpeechVoiceProfile.Formal, "audio dir/voice test.wav"),
            TestContext.Current.CancellationToken);

        Assert.Equal("audio dir/voice test.wav", result.RelativePath);
        Assert.True(File.Exists(Path.Combine(assetRoot, "audio dir", "voice test.wav")));

        var request = Assert.Single(processRunner.Requests);
        Assert.Equal("python3", request.FileName);
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("bash"));
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("sh -c"));
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("&&"));
        // Free-form text must travel in the request file, never as a shell argument.
        Assert.DoesNotContain(text, request.Arguments);
        Assert.Contains("--model", request.Arguments);
        Assert.Contains("--request", request.Arguments);
        Assert.Contains("--result", request.Arguments);
    }

    [Fact]
    public async Task SynthesizeAsync_ReportsMissingRuntime()
    {
        var provider = CreateProvider(
            new FakeProcessRunner(),
            BuildOptions(scriptPath: Path.Combine(root, "missing.py")));

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_runtime_unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_ReportsMissingModel()
    {
        var provider = CreateProvider(
            new FakeProcessRunner(),
            BuildOptions(modelPath: Path.Combine(root, "missing-model")));

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_model_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_ReportsGenerationFailure()
    {
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(exitCode: 2, success: false, writeOutput: false));

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_generation_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_ReportsScriptFailureResult()
    {
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(success: false, writeOutput: false));

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_generation_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsTimeoutAndReleasesGate()
    {
        var gate = new RecordingGpuResourceGate();
        var provider = CreateProvider(
            new FakeProcessRunner(
                _ => throw new ProcessExecutionException(
                    ProcessExecutionException.TimedOut,
                    "timed out")),
            gate: gate);

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_timeout", exception.ErrorCode);
        Assert.Equal(0, gate.ActiveLeases);
        Assert.Contains("release:speech", gate.Events);
    }

    [Fact]
    public async Task SynthesizeAsync_PropagatesCancellation()
    {
        var provider = CreateProvider(AudioGenerationTestSupport.Runner());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.SynthesizeAsync(Request(), cancellation.Token));

        Assert.False(File.Exists(Path.Combine(assetRoot, "audio", "narration", "clip.wav")));
    }

    [Fact]
    public async Task SynthesizeAsync_ReportsMissingOutput()
    {
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(writeOutput: false));

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_output_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_ReportsInvalidOutputWithoutAudio()
    {
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(),
            inspector: FakeMediaInspector.Returning(
                new MediaInspection(1.5, false, false, false, 0, 0, 0, 0)));

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_output_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_ReportsInvalidSampleRate()
    {
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(),
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(sampleRate: 44100)));

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_output_invalid", exception.ErrorCode);
    }

    private static SpeechSynthesisRequest Request() =>
        new("Teknologi lokal", SpeechVoiceProfile.Formal, "audio/narration/clip.wav");

    private SpeechSynthesisOptions BuildOptions(
        bool enabled = true,
        string? scriptPath = null,
        string? modelPath = null) =>
        new()
        {
            Enabled = enabled,
            VoxCpm2 = new SpeechSynthesisVoxCpmOptions
            {
                PythonExecutable = "python3",
                ModelPath = modelPath ?? modelDirectory,
                ScriptPath = scriptPath ?? scriptFile,
                ExpectedSampleRate = 48000
            }
        };

    private VoxCpmSpeechSynthesisProvider CreateProvider(
        IProcessRunner processRunner,
        SpeechSynthesisOptions? options = null,
        IMediaInspector? inspector = null,
        IGpuResourceGate? gate = null) =>
        new(
            Options.Create(options ?? BuildOptions()),
            new LocalAssetFileStore(Options.Create(new AssetStorageOptions { RootPath = assetRoot })),
            processRunner,
            inspector ?? FakeMediaInspector.Returning(AudioGenerationTestSupport.Audio()),
            gate ?? NoopGpuResourceGate.Instance);
}
