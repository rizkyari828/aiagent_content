using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Infrastructure.Rendering.AudioMixing;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class FfmpegAudioMixCommandPlanTests
{
    [Fact]
    public void Build_MixesNormalizedDuckedMusicIntoLimiter()
    {
        var arguments = Build(music: "/root/music.wav", narrationSeconds: 12);
        var filter = Filter(arguments);

        Assert.Contains(
            "[0:a]loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000,aformat=channel_layouts=stereo,asetpts=PTS-STARTPTS[nar]",
            filter);
        Assert.Contains("[1:a]loudnorm=I=-24:TP=-3:LRA=7", filter);
        Assert.Contains("afade=t=in:st=0:d=1", filter);
        Assert.Contains("afade=t=out:st=10.5:d=1.5", filter);
        Assert.Contains("[nar]asplit=2[narout][duckkey]", filter);
        Assert.Contains(
            "sidechaincompress=threshold=0.05:ratio=4:attack=20:release=400",
            filter);
        Assert.Contains(
            "[narout][musicduck]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.89125:level=false[a]",
            filter);

        Assert.Contains("-stream_loop", arguments);
        Assert.Contains("-1", arguments);
        Assert.Contains("[a]", arguments);
        Assert.Contains("pcm_s16le", arguments);
        Assert.Contains("/root/music.wav", arguments);
    }

    [Fact]
    public void Build_NarrationOnlyNormalizesAndLimitsWithoutMusicGraph()
    {
        var arguments = Build(narrationSeconds: 7);
        var filter = Filter(arguments);

        Assert.Contains(
            "[0:a]loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000,aformat=channel_layouts=stereo,asetpts=PTS-STARTPTS,alimiter=limit=0.89125:level=false[a]",
            filter);
        Assert.DoesNotContain("sidechaincompress", filter);
        Assert.DoesNotContain("amix", filter);
        Assert.DoesNotContain("afade", filter);
        Assert.DoesNotContain("-stream_loop", arguments);
    }

    [Fact]
    public void Build_CanDisableDuckingButKeepsMusicMix()
    {
        var arguments = Build(
            music: "/root/music.wav",
            options: new AudioMixingOptions { EnableDucking = false });
        var filter = Filter(arguments);

        Assert.DoesNotContain("sidechaincompress", filter);
        Assert.DoesNotContain("asplit", filter);
        Assert.Contains(
            "[nar][music]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.89125:level=false[a]",
            filter);
    }

    [Fact]
    public void Build_ClampsFadeOutStartForShortNarration()
    {
        var arguments = Build(
            music: "/root/music.wav",
            narrationSeconds: 1,
            options: new AudioMixingOptions { FadeOutSeconds = 1.5 });
        var filter = Filter(arguments);

        Assert.Contains("afade=t=out:st=0:d=1.5", filter);
    }

    [Fact]
    public void Build_UsesConfiguredFormatAndLimiterCeiling()
    {
        var arguments = Build(
            narrationSeconds: 5,
            options: new AudioMixingOptions
            {
                SampleRate = 44100,
                Channels = 1,
                LimiterCeilingDb = -6
            });
        var filter = Filter(arguments);

        Assert.Contains("aformat=channel_layouts=mono", filter);
        Assert.Contains("alimiter=limit=0.50119:level=false", filter);
        Assert.Equal("44100", arguments[arguments.ToList().IndexOf("-ar") + 1]);
        Assert.Equal("1", arguments[arguments.ToList().IndexOf("-ac") + 1]);
        Assert.Equal("wav", arguments[arguments.ToList().IndexOf("-f") + 1]);
    }

    [Fact]
    public void Build_KeepsArgumentsAsSeparateListItems()
    {
        var arguments = Build(music: "/root/music.wav");

        Assert.DoesNotContain(arguments, argument => argument.Contains("-i /root"));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("sh "));
        Assert.Contains("-filter_complex", arguments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_RejectsNonPositiveNarrationDuration(double narrationSeconds)
    {
        var exception = Assert.Throws<AudioMixException>(
            () => Build(narrationSeconds: narrationSeconds));

        Assert.Equal("audio_invalid_input", exception.ErrorCode);
    }

    [Fact]
    public void Build_RejectsBlankNarrationPath()
    {
        var exception = Assert.Throws<AudioMixException>(
            () => FfmpegAudioMixCommandPlan.Build(
                " ",
                null,
                "/root/out.wav",
                5,
                new AudioMixingOptions()));

        Assert.Equal("audio_invalid_input", exception.ErrorCode);
    }

    private static IReadOnlyList<string> Build(
        string? music = null,
        double narrationSeconds = 12,
        AudioMixingOptions? options = null) =>
        FfmpegAudioMixCommandPlan.Build(
            "/root/narration.wav",
            music,
            "/root/out.wav",
            narrationSeconds,
            options ?? new AudioMixingOptions());

    private static string Filter(IReadOnlyList<string> arguments) =>
        arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
}
