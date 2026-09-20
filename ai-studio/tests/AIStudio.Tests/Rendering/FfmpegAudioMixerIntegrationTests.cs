using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Infrastructure.Rendering.AudioMixing;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Real FFmpeg smoke test: generates fixture WAVs, masters them through the real
/// mixer and confirms the output is a 48 kHz stereo WAV of the narration length.
/// Skips when ffmpeg/ffprobe are not on PATH, so the default suite stays
/// dependency-free.
/// </summary>
public sealed class FfmpegAudioMixerIntegrationTests : IDisposable
{
    private readonly string root;

    public FfmpegAudioMixerIntegrationTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-audiomix-integration",
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
    public async Task MixAsync_WithRealFfmpeg_Produces48kHzStereoWav()
    {
        Assert.SkipUnless(FfmpegAvailable(), "ffmpeg/ffprobe are not available on PATH.");

        var runner = new SystemProcessRunner();
        var rendering = Options.Create(new RenderingOptions());
        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var mixer = new FfmpegAudioMixer(
            Options.Create(new AudioMixingOptions()),
            rendering,
            storage,
            new LocalAssetFileStore(storage),
            runner,
            new FfprobeMediaInspector(rendering, runner));

        var narration = Path.Combine(root, "narration.wav");
        var music = Path.Combine(root, "music.wav");
        await GenerateToneAsync(runner, narration, seconds: 6, frequency: 220, channels: 1);
        await GenerateToneAsync(runner, music, seconds: 3, frequency: 440, channels: 2);

        var result = await mixer.MixAsync(
            new AudioMixRequest("narration.wav", "music.wav", "renders/final_mix.wav"),
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(root, "renders", "final_mix.wav")));
        Assert.Equal("renders/final_mix.wav", result.RelativePath);
        Assert.Equal(48000, result.SampleRate);
        Assert.Equal(2, result.Channels);
        Assert.InRange(result.DurationSeconds, 5.5, 6.5);

        var (codec, sampleRate, channels, duration) =
            await ProbeAsync(runner, Path.Combine(root, "renders", "final_mix.wav"));
        Assert.Equal("pcm_s16le", codec);
        Assert.Equal("48000", sampleRate);
        Assert.Equal("2", channels);
        Assert.InRange(
            double.Parse(duration, CultureInfo.InvariantCulture),
            5.5,
            6.5);
    }

    private static async Task GenerateToneAsync(
        SystemProcessRunner runner,
        string path,
        int seconds,
        int frequency,
        int channels)
    {
        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi",
            "-i", $"sine=frequency={frequency}:sample_rate=44100:duration={seconds}",
            "-af", "volume=0.4",
            "-ac", channels.ToString(CultureInfo.InvariantCulture),
            "-ar", "44100",
            path
        };

        var execution = await runner.RunAsync(
            new ProcessRunRequest("ffmpeg", arguments, TimeSpan.FromSeconds(60)),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, execution.ExitCode);
    }

    private static async Task<(string Codec, string SampleRate, string Channels, string Duration)>
        ProbeAsync(SystemProcessRunner runner, string path)
    {
        var stream = await runner.RunAsync(
            new ProcessRunRequest(
                "ffprobe",
                [
                    "-v", "error",
                    "-select_streams", "a:0",
                    "-show_entries", "stream=codec_name,sample_rate,channels",
                    "-of", "csv=p=0",
                    path
                ],
                TimeSpan.FromSeconds(60)),
            TestContext.Current.CancellationToken);

        var format = await runner.RunAsync(
            new ProcessRunRequest(
                "ffprobe",
                ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                TimeSpan.FromSeconds(60)),
            TestContext.Current.CancellationToken);

        var parts = stream.StandardOutput.Trim().Split(',');
        return (parts[0], parts[1], parts[2], format.StandardOutput.Trim());
    }

    private static bool FfmpegAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception)
            when (exception is Win32Exception
                or InvalidOperationException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
