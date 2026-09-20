using System.Security.Cryptography;
using AIStudio.Application.Assets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Infrastructure.Assets;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering.AudioMixing;

/// <summary>
/// FFmpeg-backed audio mastering. Narration and background music are normalized,
/// the music is ducked under speech, both are faded and the result is limited to
/// a deterministic 48 kHz stereo WAV. CPU-only: it never acquires the GPU gate.
/// </summary>
public sealed class FfmpegAudioMixer : IAudioMixer
{
    private readonly AudioMixingOptions options;
    private readonly RenderingOptions rendering;
    private readonly IAssetFileStore assetFileStore;
    private readonly IProcessRunner processRunner;
    private readonly IMediaInspector mediaInspector;
    private readonly string outputRoot;

    public FfmpegAudioMixer(
        IOptions<AudioMixingOptions> options,
        IOptions<RenderingOptions> rendering,
        IOptions<AssetStorageOptions> assetStorage,
        IAssetFileStore assetFileStore,
        IProcessRunner processRunner,
        IMediaInspector mediaInspector)
    {
        this.options = options.Value;
        this.rendering = rendering.Value;
        this.assetFileStore = assetFileStore;
        this.processRunner = processRunner;
        this.mediaInspector = mediaInspector;
        outputRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(assetStorage.Value.RootPath));
    }

    public async Task<AudioMixOutput> MixAsync(
        AudioMixRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outputPath = ResolveOutputPath(request.RelativeOutputPath);

        // Resolve inputs through the approved asset store so path traversal,
        // symbolic links and empty files are rejected by existing rules.
        var narration = ResolveAsset(
            request.NarrationRelativePath,
            "audio_narration_not_found",
            "narration");

        var music = string.IsNullOrWhiteSpace(request.BackgroundMusicRelativePath)
            ? null
            : ResolveAsset(
                request.BackgroundMusicRelativePath!,
                "audio_music_not_found",
                "background music");

        var narrationInspection = await InspectAsync(
            narration.AbsolutePath,
            "audio_invalid_input",
            cancellationToken);
        if (!narrationInspection.HasAudio || narrationInspection.DurationSeconds <= 0)
        {
            throw new AudioMixException(
                "audio_invalid_input",
                "The narration audio is not a usable audio track.");
        }

        if (music is not null)
        {
            var musicInspection = await InspectAsync(
                music.AbsolutePath,
                "audio_invalid_input",
                cancellationToken);
            if (!musicInspection.HasAudio || musicInspection.DurationSeconds <= 0)
            {
                throw new AudioMixException(
                    "audio_invalid_input",
                    "The background music audio is not a usable audio track.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.part";

        try
        {
            var arguments = FfmpegAudioMixCommandPlan.Build(
                narration.AbsolutePath,
                music?.AbsolutePath,
                temporaryPath,
                narrationInspection.DurationSeconds,
                options);

            var execution = await RunAsync(
                rendering.FfmpegPath,
                arguments,
                cancellationToken);
            if (execution.ExitCode != 0)
            {
                throw new AudioMixException(
                    "audio_mix_failed",
                    $"FFmpeg exited with code {execution.ExitCode}: {Summarize(execution.StandardError)}");
            }

            if (!File.Exists(temporaryPath))
            {
                throw new AudioMixException(
                    "audio_output_missing",
                    "The mixed audio output was not produced.");
            }

            var probe = await InspectAsync(
                temporaryPath,
                "audio_mix_failed",
                cancellationToken);
            if (!probe.HasAudio || probe.DurationSeconds <= 0)
            {
                throw new AudioMixException(
                    "audio_output_missing",
                    "The mixed audio output has no usable audio stream.");
            }

            var contentHash = ComputeHash(temporaryPath);
            var byteSize = new FileInfo(temporaryPath).Length;
            File.Move(temporaryPath, outputPath, overwrite: true);

            return new AudioMixOutput(
                request.RelativeOutputPath.Replace('\\', '/'),
                contentHash,
                byteSize,
                probe.DurationSeconds,
                options.SampleRate,
                options.Channels);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private async Task<MediaInspection> InspectAsync(
        string path,
        string failureCode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mediaInspector.InspectAsync(path, cancellationToken);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new AudioMixException(
                "audio_ffmpeg_unavailable",
                $"Unable to start '{rendering.FfprobePath}'.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new AudioMixException(
                "audio_timeout",
                $"'{rendering.FfprobePath}' exceeded {rendering.TimeoutSeconds} seconds.",
                exception);
        }
        catch (ProcessExecutionException exception)
        {
            throw new AudioMixException(failureCode, "Audio inspection failed.", exception);
        }
    }

    private async Task<ProcessResult> RunAsync(
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
                    TimeSpan.FromSeconds(rendering.TimeoutSeconds)),
                cancellationToken);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new AudioMixException(
                "audio_ffmpeg_unavailable",
                $"Unable to start '{fileName}'.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new AudioMixException(
                "audio_timeout",
                $"'{fileName}' exceeded {rendering.TimeoutSeconds} seconds.",
                exception);
        }
    }

    private AssetFileInfo ResolveAsset(
        string relativePath,
        string notFoundCode,
        string label)
    {
        try
        {
            return assetFileStore.Register(relativePath);
        }
        catch (AssetCollectionException exception)
            when (exception.ErrorCode == "asset_file_not_found")
        {
            throw new AudioMixException(
                notFoundCode,
                $"The {label} audio was not found.");
        }
        catch (AssetCollectionException exception)
        {
            throw new AudioMixException(
                "audio_invalid_input",
                $"The {label} audio path is not valid.",
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

        if (!normalized.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new AudioMixException(
                "audio_invalid_input",
                "Audio mix output must be a .wav file.");
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

    private static AudioMixException InvalidOutputPath() =>
        new(
            "audio_invalid_input",
            "Audio mix output path must be relative to the approved artifact root.");

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
