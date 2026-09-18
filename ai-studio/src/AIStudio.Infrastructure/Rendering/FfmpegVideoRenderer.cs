using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AIStudio.Application.Rendering;
using AIStudio.Infrastructure.Assets;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

public sealed class FfmpegVideoRenderer : IVideoRenderer
{
    private static readonly string[] ProbeArguments =
    [
        "-v",
        "error",
        "-show_entries",
        "format=duration:stream=codec_type",
        "-of",
        "json"
    ];

    private readonly RenderingOptions options;
    private readonly string outputRoot;

    public FfmpegVideoRenderer(
        IOptions<RenderingOptions> options,
        IOptions<AssetStorageOptions> assetStorage)
    {
        this.options = options.Value;
        outputRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(assetStorage.Value.RootPath));
    }

    public async Task<VideoRenderOutput> RenderAsync(
        VideoRenderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Scenes is null || request.Scenes.Count == 0)
        {
            throw new RenderVideoException(
                "render_scene_count_invalid",
                "At least one scene is required.");
        }

        var outputPath = ResolveOutputPath(request.RelativeOutputPath);
        var narrationDuration = await ProbeDurationAsync(
            request.NarrationAbsolutePath,
            cancellationToken);
        var sceneDuration = FfmpegCommandPlan.SceneDurationSeconds(
            narrationDuration,
            request.Scenes.Count);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.part";

        try
        {
            var arguments = FfmpegCommandPlan.Build(
                request.Scenes,
                request.NarrationAbsolutePath,
                temporaryPath,
                sceneDuration,
                options.Width,
                options.Height,
                options.FrameRate);

            var execution = await RunProcessAsync(
                options.FfmpegPath,
                arguments,
                cancellationToken);
            if (execution.ExitCode != 0)
            {
                throw new RenderVideoException(
                    "render_failed",
                    $"FFmpeg exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
            }

            var probe = await ProbeAsync(temporaryPath, cancellationToken);
            if (!probe.HasVideo || !probe.HasAudio || probe.DurationSeconds <= 0)
            {
                throw new RenderVideoException(
                    "render_output_invalid",
                    "Rendered output is missing a valid video or audio stream.");
            }

            var contentHash = ComputeHash(temporaryPath);
            var byteSize = new FileInfo(temporaryPath).Length;
            File.Move(temporaryPath, outputPath, overwrite: true);

            return new VideoRenderOutput(
                request.RelativeOutputPath.Replace('\\', '/'),
                contentHash,
                byteSize,
                probe.DurationSeconds,
                options.Width,
                options.Height);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private async Task<double> ProbeDurationAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var probe = await ProbeAsync(path, cancellationToken);
        if (probe.DurationSeconds <= 0)
        {
            throw new RenderVideoException(
                "render_narration_duration_invalid",
                "Narration duration must be greater than zero.");
        }

        return probe.DurationSeconds;
    }

    private async Task<ProbeResult> ProbeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>(ProbeArguments) { path };
        var execution = await RunProcessAsync(
            options.FfprobePath,
            arguments,
            cancellationToken);

        if (execution.ExitCode != 0)
        {
            throw new RenderVideoException(
                "render_probe_failed",
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
            if (root.TryGetProperty("streams", out var streams)
                && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (!stream.TryGetProperty("codec_type", out var codecType))
                    {
                        continue;
                    }

                    hasVideo |= codecType.GetString() == "video";
                    hasAudio |= codecType.GetString() == "audio";
                }
            }

            return new ProbeResult(duration, hasVideo, hasAudio);
        }
        catch (JsonException exception)
        {
            throw new RenderVideoException(
                "render_probe_failed",
                "ffprobe output was not valid JSON.",
                exception);
        }
    }

    private async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception)
            when (exception is Win32Exception
                or FileNotFoundException
                or InvalidOperationException)
        {
            throw new RenderVideoException(
                "render_tool_unavailable",
                $"Unable to start '{fileName}'.",
                exception);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new RenderVideoException(
                "render_timeout",
                $"'{fileName}' exceeded {options.TimeoutSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private string ResolveOutputPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':'))
        {
            throw InvalidOutputPath();
        }

        var normalized = relativePath.Replace('\\', '/');
        if (normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            throw InvalidOutputPath();
        }

        var fullPath = Path.GetFullPath(Path.Combine(outputRoot, normalized));
        if (!fullPath.StartsWith(
            outputRoot + Path.DirectorySeparatorChar,
            StringComparison.Ordinal))
        {
            throw InvalidOutputPath();
        }

        return fullPath;
    }

    private static RenderVideoException InvalidOutputPath() =>
        new(
            "render_output_path_invalid",
            "Render output path must be relative to the approved artifact root.");

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static string Summarize(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private sealed record ProbeResult(
        double DurationSeconds,
        bool HasVideo,
        bool HasAudio);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
