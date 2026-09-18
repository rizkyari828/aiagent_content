using System.Security.Cryptography;
using AIStudio.Application.Rendering;
using AIStudio.Infrastructure.Assets;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

public sealed class FfmpegVideoRenderer : IVideoRenderer
{
    private readonly RenderingOptions options;
    private readonly IProcessRunner processRunner;
    private readonly IMediaInspector mediaInspector;
    private readonly string outputRoot;

    public FfmpegVideoRenderer(
        IOptions<RenderingOptions> options,
        IOptions<AssetStorageOptions> assetStorage,
        IProcessRunner processRunner,
        IMediaInspector mediaInspector)
    {
        this.options = options.Value;
        this.processRunner = processRunner;
        this.mediaInspector = mediaInspector;
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
                options.FrameRate,
                request.SubtitleAbsolutePath);

            var execution = await RunToolAsync(
                options.FfmpegPath,
                arguments,
                cancellationToken);
            if (execution.ExitCode != 0)
            {
                throw new RenderVideoException(
                    "render_failed",
                    $"FFmpeg exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
            }

            var probe = await InspectAsync(temporaryPath, cancellationToken);
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
        var inspection = await InspectAsync(path, cancellationToken);
        if (inspection.DurationSeconds <= 0)
        {
            throw new RenderVideoException(
                "render_narration_duration_invalid",
                "Narration duration must be greater than zero.");
        }

        return inspection.DurationSeconds;
    }

    private async Task<MediaInspection> InspectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mediaInspector.InspectAsync(path, cancellationToken);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new RenderVideoException(
                "render_tool_unavailable",
                $"Unable to start '{options.FfprobePath}'.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new RenderVideoException(
                "render_timeout",
                $"'{options.FfprobePath}' exceeded {options.TimeoutSeconds} seconds.",
                exception);
        }
        catch (ProcessExecutionException exception)
        {
            throw new RenderVideoException(
                "render_probe_failed",
                exception.Message,
                exception);
        }
    }

    private async Task<ProcessResult> RunToolAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await processRunner.RunAsync(
                new ProcessRunRequest(
                    fileName,
                    arguments,
                    TimeSpan.FromSeconds(options.TimeoutSeconds)),
                cancellationToken);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new RenderVideoException(
                "render_tool_unavailable",
                $"Unable to start '{fileName}'.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new RenderVideoException(
                "render_timeout",
                $"'{fileName}' exceeded {options.TimeoutSeconds} seconds.",
                exception);
        }
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

    private static string Summarize(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}
