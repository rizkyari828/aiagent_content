using System.Text.Json;
using AIStudio.Application.Assets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering.AudioGeneration;

/// <summary>
/// VoxCPM2 speech engine. It launches the repository-owned <c>synthesize.py</c>
/// launcher with a trusted model path and one constrained JSON request, then
/// persists the generated WAV through the approved asset store. Callers cannot
/// supply python code, executable paths, model paths, or shell fragments. The
/// heavy GPU process runs under the shared <see cref="IGpuResourceGate"/>; the
/// CPU-only persist/probe steps run after the lease is released. Failures map to
/// stable <c>speech_*</c> codes.
/// </summary>
public sealed class VoxCpmSpeechSynthesisProvider : ISpeechSynthesisProvider
{
    private const int MaxTextLength = 2000;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<SpeechVoiceProfile, string> VoiceControls =
        new Dictionary<SpeechVoiceProfile, string>
        {
            [SpeechVoiceProfile.Formal] =
                "Pria Indonesia dewasa, suara profesional dan jelas, "
                + "pengucapan Bahasa Indonesia alami, tenang dan percaya diri, "
                + "tempo sedang seperti presenter teknologi",
            [SpeechVoiceProfile.Playful] =
                "Pria Indonesia muda, suara ceria dan ramah, "
                + "pengucapan Bahasa Indonesia alami, sedikit jenaka, "
                + "ekspresif tetapi tidak berlebihan, tempo sedang",
            [SpeechVoiceProfile.Energetic] =
                "Pria Indonesia dewasa, suara bersemangat dan dinamis, "
                + "pengucapan Bahasa Indonesia alami, nada cerah dan menarik, "
                + "tempo sedikit cepat tetapi tetap jelas",
            [SpeechVoiceProfile.Documentary] =
                "Pria Indonesia dewasa, suara dokumenter yang hangat, "
                + "pengucapan Bahasa Indonesia alami, tenang, sinematik, "
                + "berwibawa tetapi ramah, tempo sedikit lambat"
        };

    private readonly SpeechSynthesisOptions options;
    private readonly IAssetFileStore assetFileStore;
    private readonly IProcessRunner processRunner;
    private readonly IMediaInspector mediaInspector;
    private readonly IGpuResourceGate gpuResourceGate;

    public VoxCpmSpeechSynthesisProvider(
        IOptions<SpeechSynthesisOptions> options,
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

    public async Task<SpeechSynthesisResult> SynthesizeAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_disabled",
                "The speech synthesis engine is not enabled.");
        }

        Validate(request);
        var vocabulary = options.VoxCpm2;

        var scriptPath = ResolveScriptPath(vocabulary.ScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new SpeechSynthesisException(
                "speech_runtime_unavailable",
                "The speech synthesis launcher was not found.");
        }

        if (string.IsNullOrWhiteSpace(vocabulary.ModelPath)
            || !Directory.Exists(vocabulary.ModelPath))
        {
            throw new SpeechSynthesisException(
                "speech_model_not_found",
                "The speech model directory was not found.");
        }

        var directory = Directory.CreateTempSubdirectory("aistudio-speech-");
        try
        {
            var requestPath = Path.Combine(directory.FullName, "request.json");
            var resultPath = Path.Combine(directory.FullName, "result.json");
            var outputPath = Path.Combine(directory.FullName, "speech.wav");

            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(
                    new
                    {
                        text = request.Text,
                        voice = VoiceControls[request.Voice],
                        inferenceTimesteps = vocabulary.InferenceTimesteps,
                        cfgValue = vocabulary.CfgValue,
                        normalize = vocabulary.Normalize,
                        outputPath
                    },
                    JsonOptions),
                cancellationToken);

            byte[] audio;

            // Hold the exclusive GPU lease across model load and inference only.
            await using (var lease = await gpuResourceGate.AcquireAsync(
                GpuWorkloads.SpeechSynthesis,
                cancellationToken))
            {
                var execution = await RunAsync(
                    vocabulary.PythonExecutable,
                    [
                        scriptPath,
                        "--model",
                        vocabulary.ModelPath,
                        "--request",
                        requestPath,
                        "--result",
                        resultPath
                    ],
                    vocabulary.TimeoutSeconds,
                    cancellationToken);

                if (execution.ExitCode != 0)
                {
                    throw new SpeechSynthesisException(
                        "speech_generation_failed",
                        $"The speech runtime exited with code {execution.ExitCode}.");
                }

                if (!await SucceededAsync(resultPath, cancellationToken))
                {
                    throw new SpeechSynthesisException(
                        "speech_generation_failed",
                        "The speech runtime reported a failure.");
                }

                if (!File.Exists(outputPath))
                {
                    throw new SpeechSynthesisException(
                        "speech_output_missing",
                        "The speech runtime did not produce an output file.");
                }

                audio = await File.ReadAllBytesAsync(outputPath, cancellationToken);
                if (audio.Length == 0)
                {
                    throw new SpeechSynthesisException(
                        "speech_output_missing",
                        "The speech output was empty.");
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
                throw new SpeechSynthesisException(
                    "speech_invalid_input",
                    "The speech output path is not valid.",
                    exception);
            }
            catch (AssetCollectionException exception)
            {
                throw new SpeechSynthesisException(
                    "speech_generation_failed",
                    "The generated speech could not be stored.",
                    exception);
            }

            var inspection = await InspectAsync(stored.AbsolutePath, cancellationToken);
            if (!inspection.HasAudio || inspection.DurationSeconds <= 0)
            {
                throw new SpeechSynthesisException(
                    "speech_output_invalid",
                    "The speech output is not a usable audio file.");
            }

            if (vocabulary.ExpectedSampleRate > 0
                && inspection.SampleRate != vocabulary.ExpectedSampleRate)
            {
                throw new SpeechSynthesisException(
                    "speech_output_invalid",
                    $"The speech output sample rate is not {vocabulary.ExpectedSampleRate} Hz.");
            }

            return new SpeechSynthesisResult(
                stored.RelativePath,
                stored.ContentHash,
                stored.ByteSize,
                inspection.DurationSeconds,
                inspection.SampleRate,
                request.Voice);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private void Validate(SpeechSynthesisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                "Speech text is required.");
        }

        if (request.Text.Length > MaxTextLength)
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                $"Speech text must be at most {MaxTextLength} characters.");
        }

        if (!Enum.IsDefined(request.Voice))
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                "The requested voice profile is not supported.");
        }

        if (string.IsNullOrWhiteSpace(request.RelativeOutputPath))
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                "A relative output path is required.");
        }

        if (request.Locale is not null
            && !request.Locale.Replace('_', '-')
                .StartsWith("id", StringComparison.OrdinalIgnoreCase))
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                "Only the Indonesian locale is supported.");
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
            throw new SpeechSynthesisException(
                "speech_runtime_unavailable",
                $"Unable to start '{fileName}'.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new SpeechSynthesisException(
                "speech_timeout",
                $"Speech synthesis exceeded {timeoutSeconds} seconds.",
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
            throw new SpeechSynthesisException(
                "speech_runtime_unavailable",
                "Unable to start the media inspector.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new SpeechSynthesisException(
                "speech_timeout",
                "Media inspection timed out.",
                exception);
        }
        catch (ProcessExecutionException exception)
        {
            throw new SpeechSynthesisException(
                "speech_output_invalid",
                "The speech output could not be inspected.",
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
