using System.Globalization;
using System.Text;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioProduction;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering.AudioProduction;

/// <summary>
/// FFmpeg-backed narration assembly. Each validated per-scene clip is placed at its
/// deterministic absolute offset with silence padding, then mixed to one 48 kHz
/// mono narration track of the exact target duration. CPU-only: it never acquires
/// the GPU resource gate.
/// </summary>
public sealed class FfmpegNarrationAssembler(
    IOptions<RenderingOptions> options,
    IProcessRunner processRunner) : IAudioNarrationAssembler
{
    private const int SampleRate = 48000;

    public async Task<byte[]> AssembleAsync(
        IReadOnlyList<NarrationSegment> segments,
        double totalDurationSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            throw new RenderVideoException(
                "audio_assembly_invalid",
                "At least one narration segment is required.");
        }

        if (totalDurationSeconds <= 0)
        {
            throw new RenderVideoException(
                "audio_assembly_invalid",
                "The assembled narration duration must be greater than zero.");
        }

        var directory = Directory.CreateTempSubdirectory("aistudio-narration-");
        try
        {
            var outputPath = Path.Combine(directory.FullName, "narration.wav");
            var arguments = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
            foreach (var segment in segments)
            {
                arguments.Add("-i");
                arguments.Add(segment.AbsolutePath);
            }

            arguments.Add("-filter_complex");
            arguments.Add(BuildFilter(segments, totalDurationSeconds));
            arguments.Add("-map");
            arguments.Add("[a]");
            arguments.Add("-c:a");
            arguments.Add("pcm_s16le");
            arguments.Add("-ar");
            arguments.Add(SampleRate.ToString(CultureInfo.InvariantCulture));
            arguments.Add("-ac");
            arguments.Add("1");
            arguments.Add("-f");
            arguments.Add("wav");
            arguments.Add(outputPath);

            ProcessResult execution;
            try
            {
                execution = await processRunner.RunAsync(
                    new ProcessRunRequest(
                        options.Value.FfmpegPath,
                        arguments,
                        TimeSpan.FromSeconds(options.Value.TimeoutSeconds)),
                    cancellationToken);
            }
            catch (ProcessExecutionException exception)
                when (exception.ErrorCode == ProcessExecutionException.StartFailed)
            {
                throw new RenderVideoException(
                    "audio_assembly_tool_unavailable",
                    $"Unable to start '{options.Value.FfmpegPath}'.",
                    exception);
            }
            catch (ProcessExecutionException exception)
                when (exception.ErrorCode == ProcessExecutionException.TimedOut)
            {
                throw new RenderVideoException(
                    "audio_assembly_timeout",
                    $"Narration assembly exceeded {options.Value.TimeoutSeconds} seconds.",
                    exception);
            }

            if (execution.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new RenderVideoException(
                    "audio_assembly_failed",
                    $"FFmpeg exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
            }

            return await File.ReadAllBytesAsync(outputPath, cancellationToken);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static string BuildFilter(
        IReadOnlyList<NarrationSegment> segments,
        double totalDurationSeconds)
    {
        var builder = new StringBuilder();
        var labels = new List<string>(segments.Count);

        for (var index = 0; index < segments.Count; index++)
        {
            var milliseconds = (long)Math.Round(Math.Max(0, segments[index].StartSeconds) * 1000);
            var label = $"s{index}";
            builder.Append(CultureInfo.InvariantCulture,
                $"[{index}:a]aresample={SampleRate},adelay={milliseconds}:all=1[{label}];");
            labels.Add(label);
        }

        var total = Format(totalDurationSeconds);
        builder.Append(CultureInfo.InvariantCulture, $"[{string.Join("][", labels)}]");
        if (segments.Count > 1)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"amix=inputs={segments.Count}:duration=longest:normalize=0,");
        }

        builder.Append(CultureInfo.InvariantCulture,
            $"apad=whole_dur={total},atrim=0:{total},asetpts=PTS-STARTPTS[a]");
        return builder.ToString();
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Summarize(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
