using System.Globalization;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Application.Rendering.Narration;
using AIStudio.Application.Scripts;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;

namespace AIStudio.Application.Jobs.GenerateAudio;

/// <summary>
/// Durable audio production with staged, retry-safe steps: per-scene narration from
/// the approved reviewed script (VoxCPM2), deterministic assembly into one narration
/// track, background music (ACE-Step) and the mastered mix (CPU-only mixer). The
/// reviewed narration is the single source of truth for speech, subtitles, scene
/// timing and choreography. Each stage records its effective input fingerprint in
/// the workspace manifest, so a retry regenerates only changed or missing stages.
/// <para>
/// This handler owns no GPU lease: the providers acquire the shared gate around
/// their own processes and the assembler/mixer are CPU-only.
/// </para>
/// </summary>
public sealed class GenerateAudioJobHandler(
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IScriptReviewRepository scripts,
    IAssetFileStore fileStore,
    AudioProductionWorkspace workspace,
    IAudioNarrationAssembler assembler,
    ISpeechSynthesisProvider speechProvider,
    IMusicGenerationProvider musicProvider,
    IAudioMixer audioMixer) : IJobHandler
{
    private const string MusicBrief =
        "instrumental modern ambient electronic background music, warm futuristic technology mood, "
        + "low energy, clean production, subtle synth textures, soft rhythmic pulse, "
        + "suitable under spoken narration, no vocals";
    private const int MusicBpm = 90;
    private const long MusicSeed = 42;
    private const double MusicTailSeconds = 2;
    private const double MinMusicSeconds = 5;
    private const double MaxMusicSeconds = 120;

    private const string SpeechStageVersion = "voxcpm2/v2";
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

        var narrations = await LoadSceneNarrationsAsync(
            job.ContentProjectId,
            storyboard,
            cancellationToken);

        var voice = ParseVoice(payload.VoiceProfile);
        var manifest = await workspace.TryReadManifestAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            cancellationToken);
        var stages = manifest is null
            ? new Dictionary<string, AudioProductionStage>()
            : new Dictionary<string, AudioProductionStage>(manifest.Stages);

        // --- 1. Per-scene narration ----------------------------------------
        var scenes = new List<GeneratedSceneNarration>(narrations.Count);
        foreach (var sceneNarration in narrations)
        {
            var stageName = AudioProductionWorkspace.SceneStageName(sceneNarration.SceneIndex);
            var fingerprint = AudioProductionFingerprint.Compute(
                stageName,
                sceneNarration.Text,
                voice.ToString(),
                payload.Locale,
                SpeechStageVersion);

            var artifact = await workspace.TryReuseAsync(
                manifest,
                stageName,
                fingerprint,
                cancellationToken);
            var reused = artifact is not null;

            if (artifact is null)
            {
                if (!speechProvider.IsEnabled)
                {
                    throw Error(
                        "audio_narration_failed",
                        "The speech synthesis provider is not enabled.");
                }

                artifact = await SynthesizeSceneAsync(
                    job,
                    payload,
                    sceneNarration,
                    voice,
                    cancellationToken);
            }

            stages[stageName] = AudioProductionWorkspace.ToStage(fingerprint, artifact);
            await PersistAsync(
                job,
                payload.StoryboardJobId,
                stages,
                manifest?.Scenes,
                manifest?.TransitionSeconds ?? 0,
                manifest?.NarrationDurationSeconds ?? 0,
                manifest?.TotalDurationSeconds ?? 0,
                cancellationToken);

            scenes.Add(new GeneratedSceneNarration(sceneNarration, artifact, reused));
        }

        // --- 2. Scene timing -------------------------------------------------
        var timing = BuildTimeline(scenes, NarrativeTiming.TransitionSeconds);
        var sceneStages = scenes
            .Select((scene, index) => new AudioSceneStage(
                scene.Narration.SceneIndex,
                scene.Artifact.RelativePath,
                scene.Artifact.ContentHash,
                scene.Artifact.ByteSize,
                scene.Artifact.SampleRate,
                scene.Artifact.Channels,
                scene.Artifact.DurationSeconds,
                timing[index].NarrationStartSeconds,
                timing[index].VisualStartSeconds,
                timing[index].VisualDurationSeconds,
                NarrativeTiming.IntroLeadSeconds,
                NarrativeTiming.OutroHoldSeconds,
                scene.Narration.Text))
            .ToArray();

        // --- 3. Assemble the narration track ---------------------------------
        var narrationFingerprint = AudioProductionFingerprint.Compute(
            AudioProductionWorkspace.NarrationStage,
            string.Join(',', scenes.Select(scene => scene.Artifact.ContentHash)),
            timing[^1].TotalDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            NarrativeTiming.TransitionSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            NarrativeTiming.Version);

        var narration = await workspace.TryReuseAsync(
            manifest,
            AudioProductionWorkspace.NarrationStage,
            narrationFingerprint,
            cancellationToken);
        var narrationReused = narration is not null;

        if (narration is null)
        {
            narration = await AssembleNarrationAsync(
                job,
                payload,
                scenes,
                timing,
                cancellationToken);
        }

        stages[AudioProductionWorkspace.NarrationStage] =
            AudioProductionWorkspace.ToStage(narrationFingerprint, narration);
        await PersistAsync(
            job,
            payload.StoryboardJobId,
            stages,
            sceneStages,
            NarrativeTiming.TransitionSeconds,
            timing[^1].NarrationEndSeconds,
            timing[^1].TotalDurationSeconds,
            cancellationToken);

        // --- 4. Background music ---------------------------------------------
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

            music = await GenerateMusicAsync(job, payload, musicDuration, cancellationToken);
        }

        stages[AudioProductionWorkspace.MusicStage] =
            AudioProductionWorkspace.ToStage(musicFingerprint, music);
        await PersistAsync(
            job,
            payload.StoryboardJobId,
            stages,
            sceneStages,
            NarrativeTiming.TransitionSeconds,
            timing[^1].NarrationEndSeconds,
            timing[^1].TotalDurationSeconds,
            cancellationToken);

        // --- 5. Final mastered mix -------------------------------------------
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
        await PersistAsync(
            job,
            payload.StoryboardJobId,
            stages,
            sceneStages,
            NarrativeTiming.TransitionSeconds,
            timing[^1].NarrationEndSeconds,
            timing[^1].TotalDurationSeconds,
            cancellationToken);

        var result = new GenerateAudioResult(
            payload.StoryboardJobId,
            ToArtifact(narration),
            ToArtifact(music),
            ToArtifact(master),
            narrationReused,
            musicReused,
            masterReused,
            scenes
                .Select((scene, index) => new GenerateAudioScene(
                    scene.Narration.SceneIndex,
                    scene.Narration.Heading,
                    scene.Narration.Text,
                    scene.Artifact.RelativePath,
                    scene.Artifact.ContentHash,
                    scene.Artifact.DurationSeconds,
                    timing[index].NarrationStartSeconds,
                    timing[index].VisualStartSeconds,
                    timing[index].VisualDurationSeconds,
                    scene.Reused))
                .ToArray(),
            NarrativeTiming.TransitionSeconds);

        return result.Serialize();
    }

    private async Task<IReadOnlyList<SceneNarrationText>> LoadSceneNarrationsAsync(
        Guid contentProjectId,
        GenerateStoryboardResult storyboard,
        CancellationToken cancellationToken)
    {
        var reviewed = await scripts.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        if (reviewed is null)
        {
            throw Error(
                "audio_narration_source_missing",
                "The content project does not have a reviewed script.");
        }

        if (reviewed.Status != ScriptReviewStatus.Approved)
        {
            throw Error(
                "audio_narration_source_not_approved",
                "Narration requires an approved reviewed script.");
        }

        try
        {
            var script = ReviewedNarrationScript.Parse(reviewed.Content);
            return script.MapToScenes(storyboard);
        }
        catch (AudioProductionException exception)
        {
            throw Error(exception.ErrorCode, exception.Message, exception);
        }
    }

    private async Task<AudioProductionArtifact> SynthesizeSceneAsync(
        ClaimedJob job,
        GenerateAudioJobPayload payload,
        SceneNarrationText narration,
        SpeechVoiceProfile voice,
        CancellationToken cancellationToken)
    {
        var relativePath = AudioProductionWorkspace.SceneNarrationPath(
            job.ContentProjectId,
            payload.StoryboardJobId,
            narration.SceneIndex);

        try
        {
            var output = await speechProvider.SynthesizeAsync(
                new SpeechSynthesisRequest(
                    narration.Text,
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

    private async Task<AudioProductionArtifact> AssembleNarrationAsync(
        ClaimedJob job,
        GenerateAudioJobPayload payload,
        IReadOnlyList<GeneratedSceneNarration> scenes,
        IReadOnlyList<SceneTimingSlice> timing,
        CancellationToken cancellationToken)
    {
        var segments = scenes
            .Select((scene, index) => new NarrationSegment(
                scene.Artifact.AbsolutePath,
                timing[index].NarrationStartSeconds))
            .ToArray();

        byte[] bytes;
        try
        {
            bytes = await assembler.AssembleAsync(
                segments,
                timing[^1].TotalDurationSeconds,
                cancellationToken);
        }
        catch (RenderVideoException exception)
        {
            throw Error("audio_narration_assembly_failed", exception.Message, exception);
        }

        var relativePath = AudioProductionWorkspace.FilePath(
            job.ContentProjectId,
            payload.StoryboardJobId,
            AudioProductionWorkspace.NarrationFileName);

        AssetFileInfo file;
        try
        {
            file = await fileStore.WriteAsync(relativePath, bytes, cancellationToken);
        }
        catch (AssetCollectionException exception)
        {
            throw Error("audio_asset_write_failed", exception.Message, exception);
        }

        return await workspace.DescribeAsync(file.RelativePath, cancellationToken);
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

    /// <summary>
    /// Scene timeline: each scene is <c>introLead + narration + outroHold</c> and
    /// consecutive scenes overlap by the render transition, so the assembled
    /// narration and the rendered video share exactly the same total duration.
    /// </summary>
    private static SceneTimingSlice[] BuildTimeline(
        IReadOnlyList<GeneratedSceneNarration> scenes,
        double transitionSeconds)
    {
        var slices = new SceneTimingSlice[scenes.Count];
        var cumulative = 0d;

        for (var index = 0; index < scenes.Count; index++)
        {
            var narration = scenes[index].Artifact.DurationSeconds;
            var sceneTotal = Math.Clamp(
                NarrativeTiming.IntroLeadSeconds + narration + NarrativeTiming.OutroHoldSeconds,
                NarrativeTiming.MinSceneSeconds,
                NarrativeTiming.MaxSceneSeconds);

            var visualStart = cumulative - (index * transitionSeconds);
            var narrationStart = visualStart + NarrativeTiming.IntroLeadSeconds;
            cumulative += sceneTotal;

            slices[index] = new SceneTimingSlice(
                visualStart,
                narrationStart,
                sceneTotal,
                narrationStart + narration);
        }

        var total = cumulative - ((scenes.Count - 1) * transitionSeconds);
        slices[^1] = slices[^1] with { TotalDurationSeconds = total };
        return slices;
    }

    private async Task PersistAsync(
        ClaimedJob job,
        Guid storyboardJobId,
        IReadOnlyDictionary<string, AudioProductionStage> stages,
        IReadOnlyList<AudioSceneStage>? scenes,
        double transitionSeconds,
        double narrationDurationSeconds,
        double totalDurationSeconds,
        CancellationToken cancellationToken)
    {
        var manifest = new AudioProductionManifest(
            AudioProductionWorkspace.Version,
            new Dictionary<string, AudioProductionStage>(stages),
            scenes,
            transitionSeconds,
            narrationDurationSeconds,
            totalDurationSeconds);

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

    private sealed record GeneratedSceneNarration(
        SceneNarrationText Narration,
        AudioProductionArtifact Artifact,
        bool Reused);

    private sealed record SceneTimingSlice(
        double VisualStartSeconds,
        double NarrationStartSeconds,
        double VisualDurationSeconds,
        double NarrationEndSeconds)
    {
        public double TotalDurationSeconds { get; init; }
    }
}
