using System.Text.Json.Nodes;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Blender 3D engine. It renders one repository-owned Blender template to a PNG
/// image sequence and encodes it to H.264 with the shared FFmpeg process boundary.
/// The template identifier selects the trusted script; only structured data
/// (palette, duration, seed) and configuration-owned render settings cross the
/// boundary. No caller-supplied Python, path, shell fragment, or node graph is
/// ever executed. Failures map to stable <c>threed_*</c> codes.
/// </summary>
public sealed class BlenderThreeDRenderingProvider : IThreeDRenderingProvider
{
    private const int MaxDurationSeconds = 60;

    private readonly BlenderOptions options;
    private readonly RenderingOptions rendering;
    private readonly IProcessRunner processRunner;
    private readonly IGpuResourceGate gpuResourceGate;

    public BlenderThreeDRenderingProvider(
        IOptions<BlenderOptions> options,
        IOptions<RenderingOptions> rendering,
        IProcessRunner processRunner,
        IGpuResourceGate gpuResourceGate)
    {
        this.options = options.Value;
        this.rendering = rendering.Value;
        this.processRunner = processRunner;
        this.gpuResourceGate = gpuResourceGate;
    }

    public bool IsEnabled => options.Enabled;

    public async Task<byte[]> RenderAsync(
        ThreeDRenderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled)
        {
            throw new RenderVideoException(
                "threed_generation_disabled",
                "The Blender 3D engine is not enabled.");
        }

        var fileName = TemplateFileName(request.Template)
            ?? throw new RenderVideoException(
                "threed_template_not_found",
                $"Blender template '{request.Template}' is not supported.");
        var scriptPath = ResolveTemplatePath(fileName);
        if (!File.Exists(scriptPath))
        {
            throw new RenderVideoException(
                "threed_template_not_found",
                $"Blender template '{fileName}' was not found.");
        }

        ValidateRequest(request);

        var directory = Directory.CreateTempSubdirectory("aistudio-blender-");
        try
        {
            var framesDirectory = Path.Combine(directory.FullName, "frames");
            var parametersPath = Path.Combine(directory.FullName, "params.json");
            var outputPath = Path.Combine(directory.FullName, "scene.mp4");

            await File.WriteAllTextAsync(
                parametersPath,
                BuildParameters(request),
                cancellationToken);

            // The Blender CUDA process is the only GPU-heavy section. The lease is
            // released before the CPU-only FFmpeg encode.
            {
                await using var lease = await gpuResourceGate.AcquireAsync(
                    GpuWorkloads.Blender,
                    cancellationToken);
                await RenderFramesAsync(scriptPath, parametersPath, framesDirectory, cancellationToken);
            }

            var frameCount = CountFrames(framesDirectory);
            if (frameCount == 0)
            {
                throw new RenderVideoException(
                    "threed_output_missing",
                    "Blender completed without producing any frames.");
            }

            await EncodeAsync(framesDirectory, frameCount, outputPath, cancellationToken);

            if (!File.Exists(outputPath))
            {
                throw new RenderVideoException(
                    "threed_output_missing",
                    "The encoded 3D clip was not produced.");
            }

            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            if (bytes.Length == 0)
            {
                throw new RenderVideoException(
                    "threed_output_missing",
                    "The encoded 3D clip is empty.");
            }

            return bytes;
        }
        finally
        {
            TryDelete(directory);
        }
    }

    /// <summary>
    /// Only structured data crosses into the trusted template. Resolution, engine,
    /// backend, samples, and frame rate are configuration-owned; palette colors
    /// reuse the single SVG design source so engines stay visually consistent.
    /// </summary>
    private string BuildParameters(ThreeDRenderRequest request)
    {
        var colors = SceneVisualSvg.GetColors(request.Palette);
        var parameters = new JsonObject
        {
            ["renderEngine"] = options.RenderEngine,
            ["computeBackend"] = options.ComputeBackend,
            ["samples"] = options.Samples,
            ["width"] = options.Width,
            ["height"] = options.Height,
            ["framesPerSecond"] = options.FramesPerSecond,
            ["durationSeconds"] = request.DurationSeconds,
            ["seed"] = request.Seed & long.MaxValue,
            ["accent"] = colors.Accent,
            ["background"] = colors.Background
        };

        return parameters.ToJsonString();
    }

    private async Task RenderFramesAsync(
        string scriptPath,
        string parametersPath,
        string framesDirectory,
        CancellationToken cancellationToken)
    {
        ProcessResult execution;
        try
        {
            execution = await processRunner.RunAsync(
                new ProcessRunRequest(
                    options.ExecutablePath,
                    [
                        "-b",
                        "--factory-startup",
                        "--python",
                        scriptPath,
                        "--",
                        "--params",
                        parametersPath,
                        "--frames",
                        framesDirectory
                    ],
                    TimeSpan.FromSeconds(options.TimeoutSeconds)),
                cancellationToken);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new RenderVideoException(
                "threed_provider_unavailable",
                $"Unable to start Blender '{options.ExecutablePath}'.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new RenderVideoException(
                "threed_generation_timeout",
                $"Blender exceeded {options.TimeoutSeconds} seconds.",
                exception);
        }

        if (execution.ExitCode != 0)
        {
            throw new RenderVideoException(
                "threed_generation_failed",
                $"Blender exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
        }
    }

    private async Task EncodeAsync(
        string framesDirectory,
        int frameCount,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ProcessResult execution;
        try
        {
            execution = await processRunner.RunAsync(
                new ProcessRunRequest(
                    rendering.FfmpegPath,
                    [
                        "-y",
                        "-hide_banner",
                        "-loglevel",
                        "error",
                        "-framerate",
                        options.FramesPerSecond.ToString(),
                        "-i",
                        Path.Combine(framesDirectory, "frame_%04d.png"),
                        "-frames:v",
                        frameCount.ToString(),
                        "-c:v",
                        "libx264",
                        "-pix_fmt",
                        "yuv420p",
                        "-movflags",
                        "+faststart",
                        outputPath
                    ],
                    TimeSpan.FromSeconds(rendering.TimeoutSeconds)),
                cancellationToken);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new RenderVideoException(
                "threed_provider_unavailable",
                $"Unable to start '{rendering.FfmpegPath}'.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new RenderVideoException(
                "threed_generation_timeout",
                $"FFmpeg exceeded {rendering.TimeoutSeconds} seconds.",
                exception);
        }

        if (execution.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new RenderVideoException(
                "threed_generation_failed",
                $"FFmpeg exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
        }
    }

    private void ValidateRequest(ThreeDRenderRequest request)
    {
        if (!Enum.IsDefined(request.Template) || request.Template == SceneThreeDTemplate.None)
        {
            throw new RenderVideoException(
                "threed_template_not_found",
                $"Blender template '{request.Template}' is not supported.");
        }

        if (!Enum.IsDefined(request.Palette))
        {
            throw new RenderVideoException(
                "threed_request_invalid",
                "The requested palette is not supported.");
        }

        if (request.DurationSeconds is <= 0 or > MaxDurationSeconds)
        {
            throw new RenderVideoException(
                "threed_request_invalid",
                $"Duration must be greater than zero and at most {MaxDurationSeconds} seconds.");
        }

        if (options.Width < 16 || options.Height < 16
            || options.Width % 2 != 0 || options.Height % 2 != 0)
        {
            throw new RenderVideoException(
                "threed_request_invalid",
                "Blender:Width and Blender:Height must be even and at least 16.");
        }

        if (options.FramesPerSecond is < 1 or > 120
            || options.Samples is < 1 or > 4096)
        {
            throw new RenderVideoException(
                "threed_request_invalid",
                "Blender frame rate or sample count is out of range.");
        }
    }

    private string ResolveTemplatePath(string fileName)
    {
        if (Path.IsPathRooted(options.TemplateDirectory))
        {
            return Path.Combine(options.TemplateDirectory, fileName);
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, options.TemplateDirectory, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), options.TemplateDirectory, fileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, options.TemplateDirectory, fileName));
    }

    private static string? TemplateFileName(SceneThreeDTemplate template) =>
        template switch
        {
            SceneThreeDTemplate.LocalAiLaptop => "local_ai_laptop.py",
            _ => null
        };

    private static int CountFrames(string framesDirectory) =>
        Directory.Exists(framesDirectory)
            ? Directory.EnumerateFiles(framesDirectory, "frame_*.png").Count()
            : 0;

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
