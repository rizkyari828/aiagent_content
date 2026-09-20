using System.Text.Json;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Infrastructure.Rendering.AudioGeneration;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class AceStepMusicGenerationProviderTests : IDisposable
{
    private readonly string root;
    private readonly string assetRoot;
    private readonly string projectRoot;
    private readonly string scriptFile;

    public AceStepMusicGenerationProviderTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-music-tests",
            Guid.NewGuid().ToString("N"));
        assetRoot = Path.Combine(root, "assets");
        projectRoot = Path.Combine(root, "ace-step");
        scriptFile = Path.Combine(root, "generate.py");
        Directory.CreateDirectory(Path.Combine(projectRoot, "checkpoints", "acestep-v15-turbo"));
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
    public async Task GenerateAsync_ThrowsWhenDisabled()
    {
        var processRunner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => CreateProvider(processRunner, BuildOptions(enabled: false)).GenerateAsync(
                Request(),
                TestContext.Current.CancellationToken));

        Assert.Equal("music_generation_disabled", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_PersistsAssetAndReportsMetadata()
    {
        var outputBytes = new byte[] { 5, 5, 5, 5 };
        var gate = new RecordingGpuResourceGate();
        string? caption = null;
        int? bpm = null;
        bool? instrumental = null;
        long? seed = null;
        int? steps = null;
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(
                audioBytes: outputBytes,
                onRequest: json =>
                {
                    caption = json.GetProperty("caption").GetString();
                    bpm = json.GetProperty("bpm").GetInt32();
                    instrumental = json.GetProperty("instrumental").GetBoolean();
                    seed = json.GetProperty("seed").GetInt64();
                    steps = json.GetProperty("inferenceSteps").GetInt32();
                }),
            gate: gate,
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, channels: 2)));

        var result = await provider.GenerateAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Equal("audio/music/bed.wav", result.RelativePath);
        Assert.Equal(RenderVideoTestData.Hash(outputBytes), result.ContentHash);
        Assert.Equal(outputBytes.Length, result.ByteSize);
        Assert.Equal(12, result.DurationSeconds);
        Assert.Equal(48000, result.SampleRate);
        Assert.Equal(2, result.Channels);
        Assert.Equal(42, result.Seed);
        Assert.True(File.Exists(Path.Combine(assetRoot, "audio", "music", "bed.wav")));

        Assert.Equal("instrumental ambient", caption);
        Assert.Equal(90, bpm);
        Assert.True(instrumental);
        Assert.Equal(42, seed);
        Assert.Equal(8, steps);

        Assert.Equal(1, gate.AcquireCount);
        Assert.Equal(0, gate.ActiveLeases);
        Assert.Contains("acquire:music", gate.Events);
        Assert.Contains("release:music", gate.Events);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    public async Task GenerateAsync_RejectsDurationOutOfRange(double duration)
    {
        var provider = CreateProvider(new FakeProcessRunner());

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(
                new MusicGenerationRequest("ambient", duration, 90, true, "audio/music/a.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("music_invalid_input", exception.ErrorCode);
    }

    [Theory]
    [InlineData(39)]
    [InlineData(221)]
    public async Task GenerateAsync_RejectsBpmOutOfRange(int bpm)
    {
        var provider = CreateProvider(new FakeProcessRunner());

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(
                new MusicGenerationRequest("ambient", 12, bpm, true, "audio/music/a.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("music_invalid_input", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_RejectsInvalidPromptAndOutputPath()
    {
        var provider = CreateProvider(new FakeProcessRunner());

        var blank = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(
                new MusicGenerationRequest("  ", 12, 90, true, "audio/music/a.wav"),
                TestContext.Current.CancellationToken));
        var tooLong = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(
                new MusicGenerationRequest(new string('a', 1001), 12, 90, true, "audio/music/a.wav"),
                TestContext.Current.CancellationToken));
        var path = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(
                new MusicGenerationRequest("ambient", 12, 90, true, " "),
                TestContext.Current.CancellationToken));

        Assert.Equal("music_invalid_input", blank.ErrorCode);
        Assert.Equal("music_invalid_input", tooLong.ErrorCode);
        Assert.Equal("music_invalid_input", path.ErrorCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenerateAsync_MapsInstrumentalMode(bool instrumental)
    {
        bool? captured = null;
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(
                onRequest: json => captured = json.GetProperty("instrumental").GetBoolean()),
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, channels: 2)));

        await provider.GenerateAsync(
            new MusicGenerationRequest("ambient", 12, 90, instrumental, "audio/music/a.wav"),
            TestContext.Current.CancellationToken);

        Assert.Equal(instrumental, captured);
    }

    [Fact]
    public async Task GenerateAsync_MapsOptionalSeed()
    {
        JsonElement? withoutSeed = null;
        JsonElement? withSeed = null;
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(
                onRequest: json => withoutSeed = json.Clone()),
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, channels: 2)));

        await provider.GenerateAsync(
            new MusicGenerationRequest("ambient", 12, 90, true, "audio/music/a.wav"),
            TestContext.Current.CancellationToken);

        provider = CreateProvider(
            AudioGenerationTestSupport.Runner(
                onRequest: json => withSeed = json.Clone()),
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, channels: 2)));

        await provider.GenerateAsync(
            new MusicGenerationRequest("ambient", 12, 90, true, "audio/music/a.wav", 1337),
            TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Null, withoutSeed!.Value.GetProperty("seed").ValueKind);
        Assert.Equal(1337, withSeed!.Value.GetProperty("seed").GetInt64());
    }

    [Fact]
    public async Task GenerateAsync_HandlesPromptWithShellMetacharactersSafely()
    {
        const string prompt = "ambient; rm -rf / \"quoted\"\nsecond line";
        string? caption = null;
        var processRunner = AudioGenerationTestSupport.Runner(
            onRequest: json => caption = json.GetProperty("caption").GetString());
        var provider = CreateProvider(
            processRunner,
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, channels: 2)));

        await provider.GenerateAsync(
            new MusicGenerationRequest(prompt, 12, 90, true, "audio dir/bed test.wav"),
            TestContext.Current.CancellationToken);

        Assert.Equal(prompt, caption);
        var request = Assert.Single(processRunner.Requests);
        Assert.Equal("python3", request.FileName);
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("bash"));
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("sh -c"));
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("&&"));
        Assert.DoesNotContain(prompt, request.Arguments);
        Assert.Contains("--project-root", request.Arguments);
        Assert.Contains("--model", request.Arguments);
    }

    [Fact]
    public async Task GenerateAsync_ReportsMissingRuntime()
    {
        var provider = CreateProvider(
            new FakeProcessRunner(),
            BuildOptions(scriptPath: Path.Combine(root, "missing.py")));

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("music_runtime_unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_ReportsMissingProjectRoot()
    {
        var provider = CreateProvider(
            new FakeProcessRunner(),
            BuildOptions(projectRoot: Path.Combine(root, "missing-root")));

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("music_runtime_unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_ReportsMissingModel()
    {
        var provider = CreateProvider(
            new FakeProcessRunner(),
            BuildOptions(model: "missing-checkpoint"));

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("music_model_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_ReportsGenerationFailure()
    {
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(exitCode: 2, success: false, writeOutput: false));

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("music_generation_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsTimeoutAndReleasesGate()
    {
        var gate = new RecordingGpuResourceGate();
        var provider = CreateProvider(
            new FakeProcessRunner(
                _ => throw new ProcessExecutionException(
                    ProcessExecutionException.TimedOut,
                    "timed out")),
            gate: gate);

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("music_timeout", exception.ErrorCode);
        Assert.Equal(0, gate.ActiveLeases);
        Assert.Contains("release:music", gate.Events);
    }

    [Fact]
    public async Task GenerateAsync_PropagatesCancellation()
    {
        var provider = CreateProvider(AudioGenerationTestSupport.Runner());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GenerateAsync(Request(), cancellation.Token));

        Assert.False(File.Exists(Path.Combine(assetRoot, "audio", "music", "bed.wav")));
    }

    [Fact]
    public async Task GenerateAsync_ReportsMissingOutput()
    {
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(writeOutput: false),
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, channels: 2)));

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("music_output_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_ReportsInvalidOutput()
    {
        var noAudio = CreateProvider(
            AudioGenerationTestSupport.Runner(),
            inspector: FakeMediaInspector.Returning(
                new MediaInspection(12, false, false, false, 0, 0, 0, 0)));
        var wrongRate = CreateProvider(
            AudioGenerationTestSupport.Runner(),
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, sampleRate: 44100, channels: 2)));
        var wrongChannels = CreateProvider(
            AudioGenerationTestSupport.Runner(),
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, channels: 1)));
        var wrongDuration = CreateProvider(
            AudioGenerationTestSupport.Runner(),
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 30, channels: 2)));

        Assert.Equal(
            "music_output_invalid",
            (await Assert.ThrowsAsync<MusicGenerationException>(
                () => noAudio.GenerateAsync(Request(), TestContext.Current.CancellationToken))).ErrorCode);
        Assert.Equal(
            "music_output_invalid",
            (await Assert.ThrowsAsync<MusicGenerationException>(
                () => wrongRate.GenerateAsync(Request(), TestContext.Current.CancellationToken))).ErrorCode);
        Assert.Equal(
            "music_output_invalid",
            (await Assert.ThrowsAsync<MusicGenerationException>(
                () => wrongChannels.GenerateAsync(Request(), TestContext.Current.CancellationToken))).ErrorCode);
        Assert.Equal(
            "music_output_invalid",
            (await Assert.ThrowsAsync<MusicGenerationException>(
                () => wrongDuration.GenerateAsync(Request(), TestContext.Current.CancellationToken))).ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_RejectsOutputPathEscapingAssetRoot()
    {
        var provider = CreateProvider(
            AudioGenerationTestSupport.Runner(),
            inspector: FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, channels: 2)));

        var exception = await Assert.ThrowsAsync<MusicGenerationException>(
            () => provider.GenerateAsync(
                new MusicGenerationRequest("ambient", 12, 90, true, "../escape.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("music_invalid_input", exception.ErrorCode);
        Assert.False(File.Exists(Path.Combine(root, "escape.wav")));
    }

    private static MusicGenerationRequest Request() =>
        new("instrumental ambient", 12, 90, true, "audio/music/bed.wav", 42);

    private MusicGenerationOptions BuildOptions(
        bool enabled = true,
        string? scriptPath = null,
        string? projectRoot = null,
        string? model = null) =>
        new()
        {
            Enabled = enabled,
            AceStep = new MusicGenerationAceStepOptions
            {
                PythonExecutable = "python3",
                ProjectRoot = projectRoot ?? this.projectRoot,
                Model = model ?? "acestep-v15-turbo",
                ScriptPath = scriptPath ?? scriptFile,
                ExpectedSampleRate = 48000,
                ExpectedChannels = 2
            }
        };

    private AceStepMusicGenerationProvider CreateProvider(
        IProcessRunner processRunner,
        MusicGenerationOptions? options = null,
        IMediaInspector? inspector = null,
        IGpuResourceGate? gate = null) =>
        new(
            Options.Create(options ?? BuildOptions()),
            new LocalAssetFileStore(Options.Create(new AssetStorageOptions { RootPath = assetRoot })),
            processRunner,
            inspector ?? FakeMediaInspector.Returning(
                AudioGenerationTestSupport.Audio(durationSeconds: 12, channels: 2)),
            gate ?? NoopGpuResourceGate.Instance);
}
