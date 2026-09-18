using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Visual Asset Engine rasterizer. Composes the SVG brief and rasterizes it to a
/// PNG with the already-installed FFmpeg librsvg decoder, reusing the shared
/// <see cref="IProcessRunner"/> boundary. No new runtime or model is introduced.
/// </summary>
public sealed class FfmpegSceneVisualRenderer : ISceneVisualRenderer
{
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

            ProcessResult execution;
            try
            {
                execution = await processRunner.RunAsync(
                    new ProcessRunRequest(
                        options.FfmpegPath,
                        ["-y", "-hide_banner", "-loglevel", "error", "-i", svgPath, "-frames:v", "1", pngPath],
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

            if (execution.ExitCode != 0 || !File.Exists(pngPath))
            {
                throw new RenderVideoException(
                    "visual_render_failed",
                    $"FFmpeg exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
            }

            return await File.ReadAllBytesAsync(pngPath, cancellationToken);
        }
        finally
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

    private static string Summarize(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}
