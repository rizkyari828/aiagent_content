using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Application.Rendering.AudioProduction;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Generous fakes for the GenerateAudio handler. Each writes a small file at the
/// requested relative path so the workspace can register and probe it; the
/// inspector models narration/music/master metadata by file name.
/// </summary>
internal sealed class FakeSpeechSynthesisProvider(string root, bool enabled = true)
    : ISpeechSynthesisProvider
{
    public bool IsEnabled { get; set; } = enabled;

    public int CallCount { get; private set; }

    public List<SpeechSynthesisRequest> Requests { get; } = [];

    public Func<SpeechSynthesisRequest, SpeechSynthesisResult>? Handler { get; set; }

    public Func<byte[]> BytesFactory { get; set; } = () => [1, 2, 3];

    public Task<SpeechSynthesisResult> SynthesizeAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        Requests.Add(request);

        if (!IsEnabled)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_disabled",
                "disabled");
        }

        if (Handler is not null)
        {
            return Task.FromResult(Handler(request));
        }

        var bytes = BytesFactory();
        FakeAudioFiles.Write(root, request.RelativeOutputPath, bytes);
        return Task.FromResult(new SpeechSynthesisResult(
            request.RelativeOutputPath,
            RenderVideoTestData.Hash(bytes),
            bytes.Length,
            10,
            48000,
            request.Voice));
    }
}

internal sealed class FakeMusicGenerationProvider(string root, bool enabled = true)
    : IMusicGenerationProvider
{
    public bool IsEnabled { get; set; } = enabled;

    public int CallCount { get; private set; }

    public List<MusicGenerationRequest> Requests { get; } = [];

    public Func<MusicGenerationRequest, MusicGenerationResult>? Handler { get; set; }

    public Task<MusicGenerationResult> GenerateAsync(
        MusicGenerationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        Requests.Add(request);

        if (!IsEnabled)
        {
            throw new MusicGenerationException("music_generation_disabled", "disabled");
        }

        if (Handler is not null)
        {
            return Task.FromResult(Handler(request));
        }

        var bytes = new byte[] { 7, 7, 7, 7 };
        FakeAudioFiles.Write(root, request.RelativeOutputPath, bytes);
        return Task.FromResult(new MusicGenerationResult(
            request.RelativeOutputPath,
            RenderVideoTestData.Hash(bytes),
            bytes.Length,
            12,
            48000,
            2,
            request.Seed));
    }
}

internal sealed class FakeAudioMixer(string root) : IAudioMixer
{
    public int CallCount { get; private set; }

    public List<AudioMixRequest> Requests { get; } = [];

    public Func<AudioMixRequest, AudioMixOutput>? Handler { get; set; }

    public Task<AudioMixOutput> MixAsync(
        AudioMixRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        Requests.Add(request);

        if (Handler is not null)
        {
            return Task.FromResult(Handler(request));
        }

        var bytes = new byte[] { 5, 5, 5 };
        FakeAudioFiles.Write(root, request.RelativeOutputPath, bytes);
        return Task.FromResult(new AudioMixOutput(
            request.RelativeOutputPath,
            RenderVideoTestData.Hash(bytes),
            bytes.Length,
            10,
            48000,
            2));
    }
}

/// <summary>Filename-driven media inspection matching the fake artifacts.</summary>
internal sealed class FakeAudioInspector : IMediaInspector
{
    public Task<MediaInspection> InspectAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var inspection = Path.GetFileName(absolutePath) switch
        {
            AudioProductionWorkspace.NarrationFileName =>
                new MediaInspection(10, false, true, false, 0, 0, 48000, 1),
            AudioProductionWorkspace.MusicFileName =>
                new MediaInspection(12, false, true, false, 0, 0, 48000, 2),
            AudioProductionWorkspace.MasterFileName =>
                new MediaInspection(10, false, true, false, 0, 0, 48000, 2),
            _ => new MediaInspection(0, false, false, false, 0, 0, 0, 0)
        };

        return Task.FromResult(inspection);
    }
}

internal static class FakeAudioFiles
{
    public static void Write(string root, string relativePath, byte[] bytes)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
