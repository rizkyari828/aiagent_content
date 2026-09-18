using System.Text.Json;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Manim animation engine. It launches the repository-owned <c>render_scene.py</c>
/// launcher with a predefined template name and a JSON parameter file, through the
/// shared <see cref="IProcessRunner"/> structured-argument boundary. Template names
/// are allowlisted here and in the launcher; no model-generated code is executed.
/// </summary>
public sealed class ProcessManimSceneRenderer : IManimSceneRenderer
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly ManimOptions options;
    private readonly IProcessRunner processRunner;

    public ProcessManimSceneRenderer(
        IOptions<ManimOptions> options,
        IProcessRunner processRunner)
    {
        this.options = options.Value;
        this.processRunner = processRunner;
    }

    public bool IsEnabled => options.Enabled;

    public async Task<byte[]> RenderAsync(
        SceneAnimationTemplate template,
        SceneAnimationParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var templateName = TemplateName(template);
        if (templateName is null)
        {
            throw new RenderVideoException(
                "manim_template_invalid",
                $"Animation template '{template}' is not supported.");
        }

        var scriptPath = ResolveScriptPath();
        if (!File.Exists(scriptPath))
        {
            throw new RenderVideoException(
                "manim_script_not_found",
                $"Manim launcher '{options.ScriptPath}' was not found.");
        }

        var directory = Directory.CreateTempSubdirectory("aistudio-manim-");
        try
        {
            var parametersPath = Path.Combine(directory.FullName, "params.json");
            var outputPath = Path.Combine(directory.FullName, "scene.mp4");
            var colors = SceneVisualSvg.GetColors(parameters.Palette);
            await File.WriteAllTextAsync(
                parametersPath,
                JsonSerializer.Serialize(
                    new
                    {
                        kicker = parameters.Kicker,
                        primaryText = parameters.PrimaryText,
                        secondaryText = parameters.SecondaryText,
                        tertiaryText = parameters.TertiaryText,
                        palette = parameters.Palette.ToString(),
                        colors = new
                        {
                            background = colors.Background,
                            backgroundAlt = colors.BackgroundAlt,
                            accent = colors.Accent,
                            text = colors.Text
                        },
                        durationSeconds = parameters.DurationSeconds
                    },
                    JsonOptions),
                cancellationToken);

            ProcessResult execution;
            try
            {
                execution = await processRunner.RunAsync(
                    new ProcessRunRequest(
                        options.PythonPath,
                        [
                            scriptPath,
                            "--template",
                            templateName,
                            "--params",
                            parametersPath,
                            "--output",
                            outputPath
                        ],
                        TimeSpan.FromSeconds(options.TimeoutSeconds)),
                    cancellationToken);
            }
            catch (ProcessExecutionException exception)
                when (exception.ErrorCode == ProcessExecutionException.StartFailed)
            {
                throw new RenderVideoException(
                    "manim_render_tool_unavailable",
                    $"Unable to start '{options.PythonPath}'.",
                    exception);
            }
            catch (ProcessExecutionException exception)
                when (exception.ErrorCode == ProcessExecutionException.TimedOut)
            {
                throw new RenderVideoException(
                    "manim_render_timeout",
                    $"Manim exceeded {options.TimeoutSeconds} seconds.",
                    exception);
            }

            if (execution.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new RenderVideoException(
                    "manim_render_failed",
                    $"Manim exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
            }

            return await File.ReadAllBytesAsync(outputPath, cancellationToken);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private string ResolveScriptPath()
    {
        if (Path.IsPathRooted(options.ScriptPath))
        {
            return options.ScriptPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, options.ScriptPath),
            Path.Combine(Directory.GetCurrentDirectory(), options.ScriptPath)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.ScriptPath));
    }

    private static string? TemplateName(SceneAnimationTemplate template) =>
        template switch
        {
            SceneAnimationTemplate.LocalAiFlow => "local_ai_flow",
            SceneAnimationTemplate.ChatFlow => "chat_flow",
            _ => null
        };

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
