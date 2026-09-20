using System.Text.Json;
using AIStudio.Application.Assets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering.AudioGeneration;

/// <summary>
/// ACE-Step music engine. It launches the repository-owned <c>generate.py</c>
/// launcher with a trusted project root and one constrained JSON request, then
/// persists the generated WAV through the approved asset store. Callers cannot
/// supply python code, executable paths, project paths, CLI flags, or shell
/// fragments. The heavy GPU process runs under the shared
/// <see cref="IGpuResourceGate"/>; the CPU-only persist/probe steps run after the
/// lease is released. Failures map to stable <c>music_*</c> codes.
/// </summary>
public sealed class AceStepMusicGenerationProvider : IMusicGenerationProvider
{
    private const int MaxPromptLength = 1000;
    private const double MinDurationSeconds = 5;
    private const double MaxDurationSeconds = 120;
    private const int MinBpm = 40;
    private const int MaxBpm = 220;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly MusicGenerationOptions options;
    private readonly IAssetFileStore assetFileStore;
    private readonly IProcessRunner processRunner;
    private readonly IMediaInspector mediaInspector;
    private readonly IGpuResourceGate gpuResourceGate;

    public AceStepMusicGenerationProvider(
        IOptions<MusicGenerationOptions> options,
        IAssetFileStore assetFileStore,
        IProcessRunner processRunner,
        IMediaInspector mediaInspector,
        IGpuResourceGate gpuResourceGate)
    {
        this.options = options.Value;
        this.assetFileStore = assetFileStore;
        this.processRunner = processRunner;
        this.mediaInspector = mediaInspector;
        this.gpuResourceGate = gpuResourceGate;
    }

    public bool IsEnabled => options.Enabled;

    public async Task<MusicGenerationResult> GenerateAsync(
        MusicGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled)
        {
            throw new MusicGenerationException(
                "music_generation_disabled",
                "The music generation engine is not enabled.");
        }

        Validate(request);
        var ace = options.AceStep;

        var scriptPath = ResolveScriptPath(ace.ScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new MusicGenerationException(
                "music_runtime_unavailable",
                "The music generation launcher was not found.");
        }

        if (string.IsNullOrWhiteSpace(ace.ProjectRoot)
            || !Directory.Exists(ace.ProjectRoot))
        {
            throw new MusicGenerationException(
                "music_runtime_unavailable",
                "The music runtime project root was not found.");
        }

        if (string.IsNullOrWhiteSpace(ace.Model)
            || !Directory.Exists(Path.Combine(ace.ProjectRoot, "checkpoints", ace.Model)))
        {
            throw new MusicGenerationException(
                "music_model_not_found",
                "The music model checkpoint was not found.");
        }

        var directory = Directory.CreateTempSubdirectory("aistudio-music-");
        try
        {
            var requestPath = Path.Combine(directory.FullName, "request.json");
            var resultPath = Path.Combine(directory.FullName, "result.json");
            var outputPath = Path.Combine(directory.FullName, "music.wav");

            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(
                    new
                    {
                        caption = request.Prompt,
                        duration = request.DurationSeconds,
                        bpm = request.Bpm,
                        instrumental = request.Instrumental,
                        seed = request.Seed,
                        inferenceSteps = ace.InferenceSteps,
                        outputPath,
                        workDir = directory.FullName
                    },
                    JsonOptions),
                cancellationToken);

            byte[] audio;

            // Hold the exclusive GPU lease across model load and generation only.
            await using (var lease = await gpuResourceGate.AcquireAsync(
                GpuWorkloads.MusicGeneration,
                cancellationToken))
            {
                var execution = await RunAsync(
                    ace.PythonExecutable,
                    [
                        scriptPath,
                        "--project-root",
                        ace.ProjectRoot,
                        "--model",
                        ace.Model,
                        "--request",
                        requestPath,
                        "--result",
                        resultPath
                    ],
                    ace.TimeoutSeconds,
                    cancellationToken);

                if (execution.ExitCode != 0)
                {
                    throw new MusicGenerationException(
                        "music_generation_failed",
                        $"The music runtime exited with code {execution.ExitCode}.");
                }

                if (!await SucceededAsync(resultPath, cancellationToken))
                {
                    throw new MusicGenerationException(
                        "music_generation_failed",
                        "The music runtime reported a failure.");
                }

                if (!File.Exists(outputPath))
                {
                    throw new MusicGenerationException(
                        "music_output_missing",
                        "The music runtime did not produce an output file.");
                }

                audio = await File.ReadAllBytesAsync(outputPath, cancellationToken);
                if (audio.Length == 0)
                {
                    throw new MusicGenerationException(
                        "music_output_missing",
                        "The music output was empty.");
                }
            }

            // CPU-only from here: persist through the approved asset store and
            // independently probe the result before reporting success.
            AssetFileInfo stored;
            try
            {
                stored = await assetFileStore.WriteAsync(
                    request.RelativeOutputPath,
                    audio,
                    cancellationToken);
            }
            catch (AssetCollectionException exception)
                when (exception.ErrorCode == "asset_path_invalid")
            {
                throw new MusicGenerationException(
                    "music_invalid_input",
                    "The music output path is not valid.",
                    exception);
            }
            catch (AssetCollectionException exception)
            {
                throw new MusicGenerationException(
                    "music_generation_failed",
                    "The generated music could not be stored.",
                    exception);
            }

            var inspection = await InspectAsync(stored.AbsolutePath, cancellationToken);
            if (!inspection.HasAudio || inspection.DurationSeconds <= 0)
            {
                throw new MusicGenerationException(
                    "music_output_invalid",
                    "The music output is not a usable audio file.");
            }

            if (ace.ExpectedSampleRate > 0
                && inspection.SampleRate != ace.ExpectedSampleRate)
            {
                throw new MusicGenerationException(
                    "music_output_invalid",
                    $"The music output sample rate is not {ace.ExpectedSampleRate} Hz.");
            }

            if (ace.ExpectedChannels > 0
                && inspection.Channels != ace.ExpectedChannels)
            {
                throw new MusicGenerationException(
                    "music_output_invalid",
                    $"The music output is not {ace.ExpectedChannels}-channel.");
            }

            var tolerance = Math.Max(1.5, request.DurationSeconds * 0.25);
            if (Math.Abs(inspection.DurationSeconds - request.DurationSeconds) > tolerance)
            {
                throw new MusicGenerationException(
                    "music_output_invalid",
                    "The music output duration does not match the request.");
            }

            return new MusicGenerationResult(
                stored.RelativePath,
                stored.ContentHash,
                stored.ByteSize,
                inspection.DurationSeconds,
                inspection.SampleRate,
                inspection.Channels,
                request.Seed);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private void Validate(MusicGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new MusicGenerationException(
                "music_invalid_input",
                "A music prompt is required.");
        }

        if (request.Prompt.Length > MaxPromptLength)
        {
            throw new MusicGenerationException(
                "music_invalid_input",
                $"The music prompt must be at most {MaxPromptLength} characters.");
        }

        if (request.DurationSeconds < MinDurationSeconds
            || request.DurationSeconds > MaxDurationSeconds)
        {
            throw new MusicGenerationException(
                "music_invalid_input",
                $"Duration must be between {MinDurationSeconds} and {MaxDurationSeconds} seconds.");
        }

        if (request.Bpm < MinBpm || request.Bpm > MaxBpm)
        {
            throw new MusicGenerationException(
                "music_invalid_input",
                $"BPM must be between {MinBpm} and {MaxBpm}.");
        }

        if (string.IsNullOrWhiteSpace(request.RelativeOutputPath))
        {
            throw new MusicGenerationException(
                "music_invalid_input",
                "A relative output path is required.");
        }
    }

    private async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            return await processRunner.RunAsync(
                new ProcessRunRequest(
                    fileName,
                    arguments,
                    TimeSpan.FromSeconds(timeoutSeconds)),
                cancellationToken);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new MusicGenerationException(
                "music_runtime_unavailable",
                $"Unable to start '{fileName}'.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new MusicGenerationException(
                "music_timeout",
                $"Music generation exceeded {timeoutSeconds} seconds.",
                exception);
        }
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
            throw new MusicGenerationException(
                "music_runtime_unavailable",
                "Unable to start the media inspector.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new MusicGenerationException(
                "music_timeout",
                "Media inspection timed out.",
                exception);
        }
        catch (ProcessExecutionException exception)
        {
            throw new MusicGenerationException(
                "music_output_invalid",
                "The music output could not be inspected.",
                exception);
        }
    }

    private static async Task<bool> SucceededAsync(
        string resultPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(resultPath))
        {
            return false;
        }

        try
        {
            var json = await File.ReadAllTextAsync(resultPath, cancellationToken);
            var result = JsonSerializer.Deserialize<ScriptResult>(json, JsonOptions);
            return result?.Success == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string ResolveScriptPath(string scriptPath)
    {
        if (Path.IsPathRooted(scriptPath))
        {
            return scriptPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, scriptPath),
            Path.Combine(Directory.GetCurrentDirectory(), scriptPath)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, scriptPath));
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

    private sealed record ScriptResult(bool Success);
}
