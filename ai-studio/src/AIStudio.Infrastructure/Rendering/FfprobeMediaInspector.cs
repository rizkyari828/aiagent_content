using System.Globalization;
using System.Text.Json;
using AIStudio.Application.Rendering;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

public sealed class FfprobeMediaInspector : IMediaInspector
{
    private static readonly string[] ProbeArguments =
    [
        "-v",
        "error",
        "-show_entries",
        "format=duration:stream=codec_type,width,height,sample_rate,channels",
        "-of",
        "json"
    ];

    private readonly RenderingOptions options;
    private readonly IProcessRunner processRunner;

    public FfprobeMediaInspector(
        IOptions<RenderingOptions> options,
        IProcessRunner processRunner)
    {
        this.options = options.Value;
        this.processRunner = processRunner;
    }

    public async Task<MediaInspection> InspectAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>(ProbeArguments) { absolutePath };

        var execution = await processRunner.RunAsync(
            new ProcessRunRequest(
                options.FfprobePath,
                arguments,
                TimeSpan.FromSeconds(options.TimeoutSeconds)),
            cancellationToken);

        if (execution.ExitCode != 0)
        {
            throw new ProcessExecutionException(
                ProcessExecutionException.MediaProbeFailed,
                $"ffprobe exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
        }

        try
        {
            using var document = JsonDocument.Parse(execution.StandardOutput);
            var root = document.RootElement;

            var duration = 0d;
            if (root.TryGetProperty("format", out var format)
                && format.TryGetProperty("duration", out var durationElement)
                && durationElement.ValueKind == JsonValueKind.String)
            {
                double.TryParse(
                    durationElement.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out duration);
            }

            var hasVideo = false;
            var hasAudio = false;
            var hasSubtitle = false;
            var width = 0;
            var height = 0;
            var sampleRate = 0;
            var channels = 0;

            if (root.TryGetProperty("streams", out var streams)
                && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (!stream.TryGetProperty("codec_type", out var codecType))
                    {
                        continue;
                    }

                    switch (codecType.GetString())
                    {
                        case "video":
                            hasVideo = true;
                            if (width <= 0 && TryGetPositiveInt(stream, "width", out var streamWidth))
                            {
                                width = streamWidth;
                            }

                            if (height <= 0 && TryGetPositiveInt(stream, "height", out var streamHeight))
                            {
                                height = streamHeight;
                            }

                            break;
                        case "audio":
                            hasAudio = true;
                            if (sampleRate <= 0
                                && TryGetIntValue(stream, "sample_rate", out var streamSampleRate))
                            {
                                sampleRate = streamSampleRate;
                            }

                            if (channels <= 0
                                && TryGetPositiveInt(stream, "channels", out var streamChannels))
                            {
                                channels = streamChannels;
                            }

                            break;
                        case "subtitle":
                            hasSubtitle = true;
                            break;
                    }
                }
            }

            return new MediaInspection(
                duration,
                hasVideo,
                hasAudio,
                hasSubtitle,
                width,
                height,
                sampleRate,
                channels);
        }
        catch (JsonException exception)
        {
            throw new ProcessExecutionException(
                ProcessExecutionException.MediaProbeFailed,
                "ffprobe output was not valid JSON.",
                exception);
        }
    }

    private static bool TryGetPositiveInt(
        JsonElement element,
        string propertyName,
        out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value)
            && value > 0;
    }

    private static bool TryGetIntValue(
        JsonElement element,
        string propertyName,
        out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value),
            _ => false
        };
    }

    private static string Summarize(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}
