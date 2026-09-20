using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Infrastructure.Rendering.AudioMixing;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class FfmpegAudioMixerTests : IDisposable
{
    private readonly string root;

    public FfmpegAudioMixerTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-audiomixer-tests",
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
    public async Task MixAsync_ProducesDeterministicOutputMetadata()
    {
        var narration = Write("narration.wav", [1, 2, 3]);
        var music = Write("music.wav", [4, 5, 6]);
        var outputBytes = new byte[] { 9, 8, 7, 6 };
        var processRunner = FakeFfmpeg.WritingOutput(outputBytes, durationSeconds: 12);
        var mixer = CreateMixer(processRunner);

        var result = await mixer.MixAsync(
            new AudioMixRequest(narration, music, "renders/out.wav"),
            TestContext.Current.CancellationToken);

        Assert.Equal("renders/out.wav", result.RelativePath);
        Assert.Equal(RenderVideoTestData.Hash(outputBytes), result.ContentHash);
        Assert.Equal(outputBytes.Length, result.ByteSize);
        Assert.Equal(12, result.DurationSeconds);
        Assert.Equal(48000, result.SampleRate);
        Assert.Equal(2, result.Channels);

        var rendersDirectory = Path.Combine(root, "renders");
        Assert.True(File.Exists(Path.Combine(rendersDirectory, "out.wav")));
        Assert.Empty(Directory.GetFiles(rendersDirectory, "*.part"));
        Assert.Equal(4, processRunner.CallCount);
    }

    [Fact]
    public async Task MixAsync_SupportsNarrationOnly()
    {
        var narration = Write("narration.wav", [1, 2, 3]);
        var processRunner = FakeFfmpeg.WritingOutput([7, 7], durationSeconds: 6);
        var mixer = CreateMixer(processRunner);

        var result = await mixer.MixAsync(
            new AudioMixRequest(narration, null, "renders/out.wav"),
            TestContext.Current.CancellationToken);

        Assert.Equal(6, result.DurationSeconds);
        Assert.Equal(3, processRunner.CallCount);
        Assert.DoesNotContain(
            processRunner.Requests,
            request => request.Arguments.Contains("-stream_loop"));
    }

    [Fact]
    public async Task MixAsync_RejectsMissingNarration()
    {
        var processRunner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest("absent.wav", null, "renders/out.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_narration_not_found", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task MixAsync_RejectsMissingMusic()
    {
        var narration = Write("narration.wav", [1]);
        var processRunner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest(narration, "absent.wav", "renders/out.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_music_not_found", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task MixAsync_RejectsInputEscapingAssetRoot()
    {
        var processRunner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest("../escape.wav", null, "renders/out.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_invalid_input", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task MixAsync_RejectsOutputPathEscapingArtifactRoot()
    {
        var narration = Write("narration.wav", [1]);
        var processRunner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest(narration, null, "../escape.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_invalid_input", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task MixAsync_RejectsNonWavOutput()
    {
        var narration = Write("narration.wav", [1]);
        var processRunner = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest(narration, null, "renders/out.mp3"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_invalid_input", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task MixAsync_ReportsMixFailedOnNonZeroExit()
    {
        var narration = Write("narration.wav", [1]);
        var mixer = CreateMixer(FakeFfmpeg.FailingRender("synthetic ffmpeg failure"));

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => mixer.MixAsync(
                new AudioMixRequest(narration, null, "renders/out.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_mix_failed", exception.ErrorCode);
        Assert.Contains("synthetic ffmpeg failure", exception.Message);
    }

    [Fact]
    public async Task MixAsync_MapsProcessStartFailureToToolUnavailable()
    {
        var narration = Write("narration.wav", [1]);
        var processRunner = new FakeProcessRunner(
            _ => throw new ProcessExecutionException(
                ProcessExecutionException.StartFailed,
                "tool not found"));

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest(narration, null, "renders/out.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_ffmpeg_unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task MixAsync_MapsProcessTimeout()
    {
        var narration = Write("narration.wav", [1]);
        var processRunner = new FakeProcessRunner(
            _ => throw new ProcessExecutionException(
                ProcessExecutionException.TimedOut,
                "timed out"));

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest(narration, null, "renders/out.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_timeout", exception.ErrorCode);
    }

    [Fact]
    public async Task MixAsync_RejectsNarrationWithoutAudioStream()
    {
        var narration = Write("narration.wav", [1]);
        var processRunner = new FakeProcessRunner(
            _ => new ProcessResult(
                0,
                """{"format":{"duration":"12"},"streams":[{"codec_type":"video"}]}""",
                string.Empty));

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest(narration, null, "renders/out.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_invalid_input", exception.ErrorCode);
    }

    [Fact]
    public async Task MixAsync_ReportsOutputMissingWhenNoAudioStream()
    {
        var narration = Write("narration.wav", [1]);
        var processRunner = new FakeProcessRunner(request =>
        {
            if (request.FileName.Contains("ffprobe", StringComparison.OrdinalIgnoreCase))
            {
                var isOutput = request.Arguments[^1].Contains(".part", StringComparison.Ordinal);
                return new ProcessResult(
                    0,
                    isOutput
                        ? """{"format":{"duration":"12"},"streams":[{"codec_type":"video"}]}"""
                        : """{"format":{"duration":"12"},"streams":[{"codec_type":"audio"}]}""",
                    string.Empty);
            }

            var output = request.Arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllBytes(output, [1, 2, 3]);
            return new ProcessResult(0, string.Empty, string.Empty);
        });

        var exception = await Assert.ThrowsAsync<AudioMixException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest(narration, null, "renders/out.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_output_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task MixAsync_PropagatesCancellationAndLeavesNoPartialOutput()
    {
        var narration = Write("narration.wav", [1]);
        var processRunner = new FakeProcessRunner(
            _ => new ProcessResult(0, string.Empty, string.Empty));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateMixer(processRunner).MixAsync(
                new AudioMixRequest(narration, null, "renders/out.wav"),
                cancellation.Token));

        var rendersDirectory = Path.Combine(root, "renders");
        if (Directory.Exists(rendersDirectory))
        {
            Assert.Empty(Directory.GetFiles(rendersDirectory, "*.part"));
        }
    }

    [Fact]
    public async Task MixAsync_HandlesPathsContainingSpaces()
    {
        var narration = Write("asset dir/my narration.wav", [1, 2, 3]);
        var music = Write("asset dir/background music.wav", [4, 5, 6]);

        var processRunner = FakeFfmpeg.WritingOutput([8, 8], durationSeconds: 5);
        var mixer = CreateMixer(processRunner);

        var result = await mixer.MixAsync(
            new AudioMixRequest(narration, music, "renders/out.wav"),
            TestContext.Current.CancellationToken);

        Assert.Equal("renders/out.wav", result.RelativePath);
        var ffmpegRequest = Assert.Single(
            processRunner.Requests,
            request => request.FileName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            Path.Combine(root, "asset dir", "my narration.wav"),
            ffmpegRequest.Arguments);
        Assert.Contains(
            Path.Combine(root, "asset dir", "background music.wav"),
            ffmpegRequest.Arguments);
    }

    private FfmpegAudioMixer CreateMixer(IProcessRunner processRunner) =>
        new(
            Options.Create(new AudioMixingOptions()),
            Options.Create(new RenderingOptions()),
            Options.Create(new AssetStorageOptions { RootPath = root }),
            new LocalAssetFileStore(Options.Create(new AssetStorageOptions { RootPath = root })),
            processRunner,
            new FfprobeMediaInspector(
                Options.Create(new RenderingOptions()),
                processRunner));

    private string Write(string relativePath, byte[] bytes)
    {
        var absolutePath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllBytes(absolutePath, bytes);
        return relativePath;
    }
}
