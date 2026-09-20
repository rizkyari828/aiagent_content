using System.Globalization;
using System.Text;
using AIStudio.Application.Rendering.AudioMixing;

namespace AIStudio.Infrastructure.Rendering.AudioMixing;

/// <summary>
/// Builds the FFmpeg argument list for a deterministic narration + background
/// music master. Every filter value comes from <see cref="AudioMixingOptions"/>
/// or the probed narration duration; callers cannot inject raw filter text.
/// </summary>
public static class FfmpegAudioMixCommandPlan
{
    public static IReadOnlyList<string> Build(
        string narrationPath,
        string? backgroundMusicPath,
        string outputPath,
        double narrationDurationSeconds,
        AudioMixingOptions options)
    {
        ArgumentNullException.ThrowIfNull(narrationPath);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(narrationPath))
        {
            throw new AudioMixException(
                "audio_invalid_input",
                "A narration path is required.");
        }

        if (narrationDurationSeconds <= 0)
        {
            throw new AudioMixException(
                "audio_invalid_input",
                "Narration duration must be greater than zero.");
        }

        var hasMusic = !string.IsNullOrWhiteSpace(backgroundMusicPath);

        var arguments = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            narrationPath
        };

        if (hasMusic)
        {
            // Loop the bed so a track shorter than the narration repeats safely;
            // the mix is bounded to the narration length.
            arguments.Add("-stream_loop");
            arguments.Add("-1");
            arguments.Add("-i");
            arguments.Add(backgroundMusicPath!);
        }

        arguments.Add("-filter_complex");
        arguments.Add(BuildFilterGraph(
            hasMusic,
            narrationDurationSeconds,
            options));
        arguments.Add("-map");
        arguments.Add("[a]");
        arguments.Add("-c:a");
        arguments.Add("pcm_s16le");
        arguments.Add("-ar");
        arguments.Add(options.SampleRate.ToString(CultureInfo.InvariantCulture));
        arguments.Add("-ac");
        arguments.Add(options.Channels.ToString(CultureInfo.InvariantCulture));
        arguments.Add("-f");
        arguments.Add("wav");
        arguments.Add(outputPath);

        return arguments;
    }

    private static string BuildFilterGraph(
        bool hasMusic,
        double narrationDurationSeconds,
        AudioMixingOptions options)
    {
        var layout = options.Channels == 1 ? "mono" : "stereo";
        var limiter = $"alimiter=limit={LinearFromDb(options.LimiterCeilingDb)}:level=false";

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture,
            $"[0:a]loudnorm=I={Format(options.NarrationLoudnessTarget)}:TP={Format(options.NarrationTruePeak)}:LRA={Format(options.NarrationLoudnessRange)},");
        builder.Append(CultureInfo.InvariantCulture,
            $"aresample={options.SampleRate},aformat=channel_layouts={layout},asetpts=PTS-STARTPTS");

        if (!hasMusic)
        {
            builder.Append(CultureInfo.InvariantCulture, $",{limiter}[a];");
            return builder.ToString();
        }

        builder.Append("[nar];");

        // The fade-out ends at the narration length, so a longer bed is trimmed
        // and a looping bed is faded out cleanly.
        var fadeOutStart = Math.Max(0, narrationDurationSeconds - options.FadeOutSeconds);
        builder.Append(CultureInfo.InvariantCulture,
            $"[1:a]loudnorm=I={Format(options.MusicLoudnessTarget)}:TP={Format(options.MusicTruePeak)}:LRA={Format(options.MusicLoudnessRange)},");
        builder.Append(CultureInfo.InvariantCulture,
            $"aresample={options.SampleRate},aformat=channel_layouts={layout},asetpts=PTS-STARTPTS,");
        builder.Append(CultureInfo.InvariantCulture,
            $"afade=t=in:st=0:d={Format(options.FadeInSeconds)},afade=t=out:st={Format(fadeOutStart)}:d={Format(options.FadeOutSeconds)}[music];");

        if (options.EnableDucking)
        {
            builder.Append("[nar]asplit=2[narout][duckkey];");
            builder.Append(CultureInfo.InvariantCulture,
                $"[music][duckkey]sidechaincompress=threshold={Format(options.DuckThreshold)}:ratio={Format(options.DuckRatio)}:attack={options.DuckAttackMs}:release={options.DuckReleaseMs}[musicduck];");
            builder.Append(CultureInfo.InvariantCulture,
                $"[narout][musicduck]amix=inputs=2:duration=first:normalize=0,{limiter}[a];");
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"[nar][music]amix=inputs=2:duration=first:normalize=0,{limiter}[a];");
        }

        return builder.ToString();
    }

    private static string LinearFromDb(double decibels) =>
        Math.Pow(10, decibels / 20d).ToString("0.#####", CultureInfo.InvariantCulture);

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
