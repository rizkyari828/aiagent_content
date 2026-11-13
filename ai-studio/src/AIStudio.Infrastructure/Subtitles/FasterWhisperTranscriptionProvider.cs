using System.Text.Json;
using AIStudio.Application.Rendering;
using AIStudio.Application.Subtitles;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Subtitles;

/// <summary>
/// Faster-Whisper transcription adapter (PRD §35). Launches the repository-owned
/// <c>transcribe.py</c> script with a constrained JSON request under
/// <see cref="IGpuResourceGate"/>, then parses the generated SRT and result JSON
/// after releasing the GPU lease.
/// </summary>
public sealed class FasterWhisperTranscriptionProvider(
    IOptions<TranscriptionOptions> options,
    IProcessRunner processRunner,
    IGpuResourceGate gpuResourceGate) : ITranscriptionProvider
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly TranscriptionOptions options = options.Value;

    public bool IsEnabled => options.Enabled;

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled)
        {
            throw new TranscriptionException(
                "transcription_disabled",
                "The transcription engine is not enabled.");
        }

        if (string.IsNullOrWhiteSpace(request.AudioAbsolutePath)
            || !File.Exists(request.AudioAbsolutePath))
        {
            throw new TranscriptionException(
                "transcription_invalid_input",
                "The narration audio file does not exist.");
        }

        var settings = options.FasterWhisper;
        var scriptPath = ResolveScriptPath(settings.ScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new TranscriptionException(
                "transcription_runtime_unavailable",
                "The transcription launcher script was not found.");
        }

        var language = string.IsNullOrWhiteSpace(request.Language)
            ? settings.DefaultLanguage
            : request.Language.Trim();

        var directory = Directory.CreateTempSubdirectory("aistudio-transcribe-");
        try
        {
            var requestPath = Path.Combine(directory.FullName, "request.json");
            var resultPath = Path.Combine(directory.FullName, "result.json");
            var outputPath = Path.Combine(directory.FullName, "subtitles.srt");

            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(
                    new
                    {
                        audioPath = request.AudioAbsolutePath,
                        outputPath,
                        model = settings.Model,
                        device = settings.Device,
                        computeType = settings.ComputeType,
                        language
                    },
                    JsonOptions),
                cancellationToken);

            // Hold the exclusive GPU lease only across the Python transcription run.
            await using (var lease = await gpuResourceGate.AcquireAsync(
                GpuWorkloads.Transcription,
                cancellationToken))
            {
                var execution = await processRunner.RunAsync(
                    new ProcessRunRequest(
                        settings.PythonExecutable,
                        [
                            scriptPath,
                            "--request",
                            requestPath,
                            "--result",
                            resultPath
                        ],
                        TimeSpan.FromSeconds(settings.TimeoutSeconds)),
                    cancellationToken);

                if (execution.ExitCode != 0)
                {
                    throw new TranscriptionException(
                        "transcription_failed",
                        $"The transcription runtime exited with code {execution.ExitCode}.");
                }
            }

            if (!File.Exists(resultPath) || !File.Exists(outputPath))
            {
                throw new TranscriptionException(
                    "transcription_output_missing",
                    "The transcription runtime did not produce an SRT output file.");
            }

            var resultMetadata = await ReadResultAsync(resultPath, cancellationToken);
            if (!resultMetadata.Ok)
            {
                throw new TranscriptionException(
                    "transcription_failed",
                    resultMetadata.Error ?? "The transcription runtime reported a failure.");
            }

            var srtContent = await File.ReadAllTextAsync(outputPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(srtContent))
            {
                throw new TranscriptionException(
                    "transcription_output_missing",
                    "The transcription runtime produced an empty SRT file.");
            }

            return new TranscriptionResult(
                srtContent,
                resultMetadata.Language ?? language,
                resultMetadata.SegmentCount);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static string ResolveScriptPath(string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

    private static async Task<LauncherResult> ReadResultAsync(
        string resultPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(resultPath, cancellationToken);
            return JsonSerializer.Deserialize<LauncherResult>(json, JsonOptions)
                ?? new LauncherResult(false, null, 0, "Empty launcher result.");
        }
        catch (JsonException exception)
        {
            throw new TranscriptionException(
                "transcription_failed",
                "The transcription runtime wrote an invalid result JSON.",
                exception);
        }
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            if (directory.Exists)
            {
                directory.Delete(recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record LauncherResult(
        bool Ok,
        string? Language,
        int SegmentCount,
        string? Error);
}
