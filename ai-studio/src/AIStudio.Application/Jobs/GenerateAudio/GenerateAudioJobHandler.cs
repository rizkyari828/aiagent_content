using System.Globalization;
using System.Text;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.GenerateAudio;

/// <summary>
/// Durable audio production with three internally staged, retry-safe steps:
/// narration (VoxCPM2), background music (ACE-Step) and the final mastered mix
/// (the CPU-only mixer). Each stage records its effective input fingerprint in the
/// workspace manifest, so a retry reuses every artifact whose inputs are unchanged
/// and regenerates only the missing or invalidated stages.
/// <para>
/// This handler owns no GPU lease: the speech and music providers acquire the
/// shared resource gate around their own processes, and the mixer is CPU-only.
/// </para>
/// </summary>
public sealed class GenerateAudioJobHandler(
    IContentProjectReader contentProjects,
    IJobReader jobs,
    AudioProductionWorkspace workspace,
    ISpeechSynthesisProvider speechProvider,
    IMusicGenerationProvider musicProvider,
    IAudioMixer audioMixer) : IJobHandler
{
    // Fixed, deterministic v2 audio defaults. A changed value changes the stage
    // fingerprint and therefore forces regeneration of exactly that stage.
    private const string MusicBrief =
        "instrumental modern ambient electronic background music, warm futuristic technology mood, "
        + "low energy, clean production, subtle synth textures, soft rhythmic pulse, "
        + "suitable under spoken narration, no vocals";
    private const int MusicBpm = 90;
    private const long MusicSeed = 42;
    private const double MusicTailSeconds = 2;
    private const double MinMusicSeconds = 5;
    private const double MaxMusicSeconds = 120;

    // Stage versions force regeneration when the production behavior changes even
    // though the literal content inputs did not.
    private const string SpeechStageVersion = "voxcpm2/v1";
    private const string MusicStageVersion = "acestep-turbo/v1";
    private const string MixStageVersion = "audio-mixing/v1";

    public bool CanHandle(JobType type) => type == JobType.GenerateAudio;

    public async Task<string> ExecuteAsync(
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        if (job.Type != JobType.GenerateAudio)
        {
            throw Error(
                "audio_wrong_job_type",
                $"GenerateAudio handler cannot execute job type {job.Type}.");
        }

        var payload = GenerateAudioJobPayload.Deserialize(job.Payload);
        if (payload.ContentProjectId != job.ContentProjectId)
        {
            throw Error(
                "audio_invalid_payload",
                "GenerateAudio payload contentProjectId does not match the claimed job.");
        }

        var project = await contentProjects.FindByIdAsync(
            job.ContentProjectId,
            cancellationToken);
        if (project is null)
        {
            throw Error(
                "content_project_not_found",
                $"Content project '{job.ContentProjectId}' was not found.");
        }

        var storyboard = await LoadStoryboardAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            cancellationToken);

        var voice = ParseVoice(payload.VoiceProfile);
        var narrationText = BuildNarrationText(storyboard);

        var manifest = await workspace.TryReadManifestAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            cancellationToken);
        var stages = manifest is null
            ? new Dictionary<string, AudioProductionStage>()
            : new Dictionary<string, AudioProductionStage>(manifest.Stages);

        // --- 1. Narration ---------------------------------------------------
        var narrationFingerprint = AudioProductionFingerprint.Compute(
            AudioProductionWorkspace.NarrationStage,
            narrationText,
            voice.ToString(),
            payload.Locale,
            SpeechStageVersion);

        var narration = await workspace.TryReuseAsync(
            manifest,
            AudioProductionWorkspace.NarrationStage,
            narrationFingerprint,
            cancellationToken);
        var narrationReused = narration is not null;

        if (narration is null)
        {
            if (!speechProvider.IsEnabled)
            {
                throw Error(
                    "audio_narration_failed",
                    "The speech synthesis provider is not enabled.");
            }

            narration = await SynthesizeNarrationAsync(
                job,
                payload,
                narrationText,
                voice,
                cancellationToken);
        }

        stages[AudioProductionWorkspace.NarrationStage] =
            AudioProductionWorkspace.ToStage(narrationFingerprint, narration);
        await PersistAsync(job, payload.StoryboardJobId, stages, cancellationToken);

        // --- 2. Background music -------------------------------------------
        var musicDuration = ResolveMusicDuration(narration.DurationSeconds);
        var musicFingerprint = AudioProductionFingerprint.Compute(
            AudioProductionWorkspace.MusicStage,
            MusicBrief,
            musicDuration.ToString("0.###", CultureInfo.InvariantCulture),
            MusicBpm.ToString(CultureInfo.InvariantCulture),
            "instrumental",
            MusicSeed.ToString(CultureInfo.InvariantCulture),
            MusicStageVersion);

        var music = await workspace.TryReuseAsync(
            manifest,
            AudioProductionWorkspace.MusicStage,
            musicFingerprint,
            cancellationToken);
        var musicReused = music is not null;

        if (music is null)
        {
            if (!musicProvider.IsEnabled)
            {
                throw Error(
                    "audio_music_failed",
                    "The music generation provider is not enabled.");
            }

            music = await GenerateMusicAsync(
                job,
                payload,
                musicDuration,
                cancellationToken);
        }

        stages[AudioProductionWorkspace.MusicStage] =
            AudioProductionWorkspace.ToStage(musicFingerprint, music);
        await PersistAsync(job, payload.StoryboardJobId, stages, cancellationToken);

        // --- 3. Final mastered mix -----------------------------------------
        var masterFingerprint = AudioProductionFingerprint.Compute(
            AudioProductionWorkspace.MasterStage,
            narration.ContentHash,
            music.ContentHash,
            MixStageVersion);

        var master = await workspace.TryReuseAsync(
            manifest,
            AudioProductionWorkspace.MasterStage,
            masterFingerprint,
            cancellationToken);
        var masterReused = master is not null;

        if (master is null)
        {
            master = await MixAsync(job, payload, narration, music, cancellationToken);
        }

        stages[AudioProductionWorkspace.MasterStage] =
            AudioProductionWorkspace.ToStage(masterFingerprint, master);
        await PersistAsync(job, payload.StoryboardJobId, stages, cancellationToken);

        var result = new GenerateAudioResult(
            payload.StoryboardJobId,
            ToArtifact(narration),
            ToArtifact(music),
            ToArtifact(master),
            narrationReused,
            musicReused,
            masterReused);

        return result.Serialize();
    }

    private async Task<AudioProductionArtifact> SynthesizeNarrationAsync(
        ClaimedJob job,
        GenerateAudioJobPayload payload,
        string narrationText,
        SpeechVoiceProfile voice,
        CancellationToken cancellationToken)
    {
        var relativePath = AudioProductionWorkspace.FilePath(
            job.ContentProjectId,
            payload.StoryboardJobId,
            AudioProductionWorkspace.NarrationFileName);

        try
        {
            var output = await speechProvider.SynthesizeAsync(
                new SpeechSynthesisRequest(
                    narrationText,
                    voice,
                    relativePath,
                    payload.Locale),
                cancellationToken);

            return await workspace.DescribeAsync(output.RelativePath, cancellationToken);
        }
        catch (SpeechSynthesisException exception)
        {
            throw Error("audio_narration_failed", "Narration generation failed.", exception);
        }
        catch (AudioProductionException exception)
        {
            throw Error(exception.ErrorCode, exception.Message, exception);
        }
    }

    private async Task<AudioProductionArtifact> GenerateMusicAsync(
        ClaimedJob job,
        GenerateAudioJobPayload payload,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        var relativePath = AudioProductionWorkspace.FilePath(
            job.ContentProjectId,
            payload.StoryboardJobId,
            AudioProductionWorkspace.MusicFileName);

        try
        {
            var output = await musicProvider.GenerateAsync(
                new MusicGenerationRequest(
                    MusicBrief,
                    durationSeconds,
                    MusicBpm,
                    Instrumental: true,
                    relativePath,
                    MusicSeed),
                cancellationToken);

            return await workspace.DescribeAsync(output.RelativePath, cancellationToken);
        }
        catch (MusicGenerationException exception)
        {
            throw Error("audio_music_failed", "Music generation failed.", exception);
        }
        catch (AudioProductionException exception)
        {
            throw Error(exception.ErrorCode, exception.Message, exception);
        }
    }

    private async Task<AudioProductionArtifact> MixAsync(
        ClaimedJob job,
        GenerateAudioJobPayload payload,
        AudioProductionArtifact narration,
        AudioProductionArtifact music,
        CancellationToken cancellationToken)
    {
        var relativePath = AudioProductionWorkspace.FilePath(
            job.ContentProjectId,
            payload.StoryboardJobId,
            AudioProductionWorkspace.MasterFileName);

        try
        {
            var output = await audioMixer.MixAsync(
                new AudioMixRequest(narration.RelativePath, music.RelativePath, relativePath),
                cancellationToken);

            return await workspace.DescribeAsync(output.RelativePath, cancellationToken);
        }
        catch (AudioMixException exception)
        {
            throw Error("audio_mix_failed", "Final audio mixing failed.", exception);
        }
        catch (AudioProductionException exception)
        {
            throw Error(exception.ErrorCode, exception.Message, exception);
        }
    }

    private async Task PersistAsync(
        ClaimedJob job,
        Guid storyboardJobId,
        IReadOnlyDictionary<string, AudioProductionStage> stages,
        CancellationToken cancellationToken)
    {
        var manifest = new AudioProductionManifest(
            AudioProductionWorkspace.Version,
            new Dictionary<string, AudioProductionStage>(stages));

        try
        {
            await workspace.SaveManifestAsync(
                job.ContentProjectId,
                storyboardJobId,
                manifest,
                cancellationToken);
        }
        catch (AssetCollectionException exception)
        {
            throw Error(
                "audio_artifact_invalid",
                "The audio production manifest could not be stored.",
                exception);
        }
    }

    private async Task<GenerateStoryboardResult> LoadStoryboardAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GenerateAudioWorkflow.RequireStoryboardAsync(
                contentProjectId,
                storyboardJobId,
                jobs,
                cancellationToken);
        }
        catch (AudioProductionException exception)
        {
            throw Error(exception.ErrorCode, exception.Message, exception);
        }
    }

    /// <summary>
    /// Deterministic narration text: the storyboard title followed by each scene
    /// heading. This is always available for a completed storyboard and needs no
    /// extra AI call.
    /// </summary>
    private static string BuildNarrationText(GenerateStoryboardResult storyboard)
    {
        var builder = new StringBuilder(storyboard.Title.Trim().TrimEnd('.'));
        builder.Append('.');

        foreach (var scene in storyboard.Scenes)
        {
            var heading = scene.Heading.Trim().TrimEnd('.');
            if (heading.Length == 0)
            {
                continue;
            }

            builder.Append(' ').Append(heading).Append('.');
        }

        return builder.ToString().Trim();
    }

    private static double ResolveMusicDuration(double narrationSeconds) =>
        Math.Clamp(
            Math.Ceiling(narrationSeconds) + MusicTailSeconds,
            MinMusicSeconds,
            MaxMusicSeconds);

    private static SpeechVoiceProfile ParseVoice(string value)
    {
        if (!Enum.TryParse<SpeechVoiceProfile>(value, ignoreCase: true, out var voice)
            || !Enum.IsDefined(voice))
        {
            throw Error(
                "audio_invalid_input",
                "The requested voice profile is not supported.");
        }

        return voice;
    }

    private static GenerateAudioArtifact ToArtifact(AudioProductionArtifact artifact) =>
        new(
            artifact.RelativePath,
            artifact.ContentHash,
            artifact.ByteSize,
            artifact.DurationSeconds,
            artifact.SampleRate,
            artifact.Channels);

    private static JobExecutionException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
