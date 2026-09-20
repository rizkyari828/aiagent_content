using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Visual Asset Engine rasterizer/compositor. It composes SVG (stills or
/// choreographed motion-graphics frames) and rasterizes them with the
/// already-installed FFmpeg librsvg decoder, then encodes frames to H.264 with the
/// shared <see cref="IProcessRunner"/> boundary. No new runtime or model is used.
/// </summary>
public sealed class FfmpegSceneVisualRenderer : ISceneVisualRenderer
{
    private const int AnimationFps = 12;
    private const double OverlayInSeconds = 1.2;

    private readonly RenderingOptions options;
    private readonly IProcessRunner processRunner;

    public FfmpegSceneVisualRenderer(
        IOptions<RenderingOptions> options,
        IProcessRunner processRunner)
    {
        this.options = options.Value;
        this.processRunner = processRunner;
    }

    public async Task<byte[]> RenderPngAsync(
        SceneVisualBrief brief,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        var svg = SceneVisualSvg.Compose(brief);

        var directory = Directory.CreateTempSubdirectory("aistudio-visual-");
        try
        {
            var svgPath = Path.Combine(directory.FullName, "scene.svg");
            var pngPath = Path.Combine(directory.FullName, "scene.png");
            await File.WriteAllTextAsync(svgPath, svg, cancellationToken);
            await RasterizeAsync(svgPath, pngPath, cancellationToken);
            return await File.ReadAllBytesAsync(pngPath, cancellationToken);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    public async Task<byte[]> RenderAnimationAsync(
        SceneVisualBrief brief,
        SceneChoreography choreography,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(choreography);

        var directory = Directory.CreateTempSubdirectory("aistudio-visual-anim-");
        try
        {
            var frames = Math.Max(2, (int)Math.Ceiling(durationSeconds * AnimationFps));
            for (var index = 0; index < frames; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var time = index / (double)AnimationFps;
                var state = ChoreographyEvaluator.Evaluate(choreography, time, durationSeconds);
                var svgPath = Path.Combine(directory.FullName, $"frame_{index:0000}.svg");
                var pngPath = Path.Combine(directory.FullName, $"frame_{index:0000}.png");
                await File.WriteAllTextAsync(
                    svgPath,
                    SceneVisualSvg.ComposeFrame(brief, state),
                    cancellationToken);
                await RasterizeAsync(svgPath, pngPath, cancellationToken);
            }

            var outputPath = Path.Combine(directory.FullName, "scene.mp4");
            await RunFfmpegAsync(
                [
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-framerate", AnimationFps.ToString(),
                    "-i", Path.Combine(directory.FullName, "frame_%04d.png"),
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "20",
                    "-pix_fmt", "yuv420p", "-r", "30",
                    outputPath
                ],
                cancellationToken);

            return await ReadOutputAsync(outputPath, cancellationToken);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    public async Task<byte[]> RenderImageMotionAsync(
        byte[] backgroundPng,
        string overlayLabel,
        SceneVisualPalette palette,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backgroundPng);
        if (backgroundPng.Length == 0)
        {
            throw new RenderVideoException(
                "visual_render_failed",
                "The AI image background is empty.");
        }

        var directory = Directory.CreateTempSubdirectory("aistudio-visual-motion-");
        try
        {
            var backgroundPath = Path.Combine(directory.FullName, "background.png");
            await File.WriteAllBytesAsync(backgroundPath, backgroundPng, cancellationToken);

            var frames = Math.Max(2, (int)Math.Ceiling(durationSeconds * AnimationFps));
            for (var index = 0; index < frames; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var time = index / (double)AnimationFps;
                var reveal = EaseOut(Math.Clamp(time / OverlayInSeconds, 0, 1));
                var opacity = reveal;
                var scale = 0.9 + (0.1 * reveal) + (0.02 * Math.Sin(time * Math.PI));
                var svgPath = Path.Combine(directory.FullName, $"overlay_{index:0000}.svg");
                var pngPath = Path.Combine(directory.FullName, $"overlay_{index:0000}.png");
                await File.WriteAllTextAsync(
                    svgPath,
                    SceneVisualSvg.ComposeOverlay(overlayLabel, palette, opacity, scale),
                    cancellationToken);
                await RasterizeAsync(svgPath, pngPath, cancellationToken);
            }

            var outputPath = Path.Combine(directory.FullName, "motion.mp4");
            await RunFfmpegAsync(
                [
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-loop", "1", "-framerate", AnimationFps.ToString(), "-i", backgroundPath,
                    "-framerate", AnimationFps.ToString(),
                    "-i", Path.Combine(directory.FullName, "overlay_%04d.png"),
                    "-filter_complex",
                    "[0:v]scale=1280:720:force_original_aspect_ratio=increase,"
                    + "crop=1280:720,"
                    + "zoompan=z='min(zoom+0.0006,1.10)':d=1:"
                    + "x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s=1280x720:fps=12[bg];"
                    + "[bg][1:v]overlay=0:0:format=auto,format=yuv420p[v]",
                    "-map", "[v]",
                    "-frames:v", frames.ToString(),
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-r", "30",
                    outputPath
                ],
                cancellationToken);

            return await ReadOutputAsync(outputPath, cancellationToken);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private async Task RasterizeAsync(
        string svgPath,
        string pngPath,
        CancellationToken cancellationToken)
    {
        await RunFfmpegAsync(
            ["-y", "-hide_banner", "-loglevel", "error", "-i", svgPath, "-frames:v", "1", pngPath],
            cancellationToken);
    }

    private async Task RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessResult execution;
        try
        {
            execution = await processRunner.RunAsync(
                new ProcessRunRequest(
                    options.FfmpegPath,
                    arguments,
                    TimeSpan.FromSeconds(options.TimeoutSeconds)),
                cancellationToken);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new RenderVideoException(
                "visual_render_tool_unavailable",
                $"Unable to start '{options.FfmpegPath}'.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new RenderVideoException(
                "visual_render_timeout",
                $"'{options.FfmpegPath}' exceeded {options.TimeoutSeconds} seconds.",
                exception);
        }

        if (execution.ExitCode != 0)
        {
            throw new RenderVideoException(
                "visual_render_failed",
                $"FFmpeg exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
        }
    }

    private static async Task<byte[]> ReadOutputAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(outputPath))
        {
            throw new RenderVideoException(
                "visual_render_failed",
                "FFmpeg completed without producing a visual clip.");
        }

        var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
        if (bytes.Length == 0)
        {
            throw new RenderVideoException(
                "visual_render_failed",
                "The rendered visual clip is empty.");
        }

        return bytes;
    }

    private static double EaseOut(double value) => 1 - Math.Pow(1 - value, 3);

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
