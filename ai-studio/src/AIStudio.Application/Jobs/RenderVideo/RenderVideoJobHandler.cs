using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Narration;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Application.Subtitles;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.RenderVideo;

/// <summary>
/// Durable render. The audio input is the mastered audio produced by GenerateAudio
/// when it exists and validates; otherwise the renderer falls back to the legacy
/// per-project narration track, so pre-orbestration projects keep working
/// unchanged. Scene assets, transitions, subtitles and per-scene timing are
/// untouched.
/// </summary>
public sealed class RenderVideoJobHandler(
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IAssetRepository assets,
    INarrationRepository narrations,
    ISubtitleRepository subtitles,
    IAssetFileStore fileStore,
    IVideoRenderer renderer,
    AudioProductionWorkspace audioWorkspace) : IJobHandler
{
    public bool CanHandle(JobType type) => type == JobType.RenderVideo;

    public async Task<string> ExecuteAsync(
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        if (job.Type != JobType.RenderVideo)
        {
            throw new JobExecutionException(
                "render_video_wrong_job_type",
                $"RenderVideo handler cannot execute job type {job.Type}.");
        }

        var payload = RenderVideoJobPayload.Deserialize(job.Payload);
        if (payload.ContentProjectId != job.ContentProjectId)
        {
            throw new JobExecutionException(
                "render_video_invalid_payload",
                "RenderVideo payload contentProjectId does not match the claimed job.");
        }

        var project = await contentProjects.FindByIdAsync(
            job.ContentProjectId,
            cancellationToken);
        if (project is null)
        {
            throw new JobExecutionException(
                "content_project_not_found",
                $"Content project '{job.ContentProjectId}' was not found.");
        }

        var storyboard = await FindStoryboardAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            cancellationToken);

        var sceneInputs = await BuildSceneInputsAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            storyboard,
            cancellationToken);

        var audio = await ResolveAudioAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            cancellationToken);

        var subtitle = await ResolveSubtitleAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            storyboard,
            cancellationToken);

        var relativeOutput = BuildOutputPath(
            job.ContentProjectId,
            payload.StoryboardJobId,
            payload.Variant);

        VideoRenderOutput output;
        try
        {
            output = await renderer.RenderAsync(
                new VideoRenderRequest(
                    sceneInputs,
                    audio.AbsolutePath,
                    relativeOutput,
                    subtitle.Path,
                    subtitle.CueTexts),
                cancellationToken);
        }
        catch (RenderVideoException exception)
        {
            throw new JobExecutionException(
                exception.ErrorCode,
                exception.Message,
                exception);
        }

        var result = new RenderVideoResult(
            output.RelativePath,
            output.ContentHash,
            output.ByteSize,
            output.DurationSeconds,
            output.Width,
            output.Height,
            payload.StoryboardJobId,
            audio.NarrationTrackId,
            sceneInputs.Count,
            audio.NarrationContentHash,
            subtitle.TrackId,
            subtitle.ContentHash,
            subtitle.Path is not null,
            audio.Source,
            audio.MasteredPath,
            audio.MasteredContentHash);

        return result.Serialize();
    }

    private async Task<IReadOnlyList<SceneMediaInput>> BuildSceneInputsAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        GenerateStoryboardResult storyboard,
        CancellationToken cancellationToken)
    {
        var registered = await assets.ListByProjectAsync(
            contentProjectId,
            cancellationToken);
        var byScene = registered.ToDictionary(asset => asset.SceneIndex);
        var sceneInputs = new List<SceneMediaInput>(storyboard.Scenes.Count);

        for (var sceneIndex = 0; sceneIndex < storyboard.Scenes.Count; sceneIndex++)
        {
            if (!byScene.TryGetValue(sceneIndex, out var asset))
            {
                throw new JobExecutionException(
                    "render_assets_incomplete",
                    $"Scene {sceneIndex} has no registered asset.");
            }

            if (asset.SourceJobId != storyboardJobId)
            {
                throw new JobExecutionException(
                    "render_asset_storyboard_mismatch",
                    $"Scene {sceneIndex} asset belongs to a different storyboard.");
            }

            var assetFile = ReadVerified(
                asset.Path,
                asset.ContentHash,
                "render_asset_unreadable",
                "render_asset_hash_mismatch");

            // Programmatic SVG/Manim visuals are text-heavy; the transition policy
            // fades through background for them. Manual assets stay photographic.
            var visualKind = SceneAssetProvenance.IsGeneratedGraphic(asset.Source)
                ? SceneVisualKind.Graphic
                : SceneVisualKind.Photographic;

            // Narration-aware timing fallback: longer scene text gets more time.
            var scene = storyboard.Scenes[sceneIndex];
            var weight = SceneTiming.WeightFor(scene.Heading, scene.Visual);

            sceneInputs.Add(new SceneMediaInput(
                assetFile.AbsolutePath,
                asset.Type,
                VisualKind: visualKind,
                Weight: weight));
        }

        return sceneInputs;
    }

    /// <summary>
    /// Prefers the mastered audio from the GenerateAudio workspace. When it is
    /// missing or no longer valid, the legacy narration track is required.
    /// </summary>
    private async Task<ResolvedAudio> ResolveAudioAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        CancellationToken cancellationToken)
    {
        var manifest = await audioWorkspace.TryReadManifestAsync(
            contentProjectId,
            storyboardJobId,
            cancellationToken);
        var master = await audioWorkspace.TryResolveStageAsync(
            manifest,
            AudioProductionWorkspace.MasterStage,
            cancellationToken);

        if (master is not null)
        {
            return new ResolvedAudio(
                master.AbsolutePath,
                RenderAudioSources.Mastered,
                NarrationTrackId: null,
                NarrationContentHash: null,
                master.RelativePath,
                master.ContentHash);
        }

        var narration = await narrations.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        if (narration is null)
        {
            throw new JobExecutionException(
                "render_audio_missing",
                $"Content project '{contentProjectId}' does not have narration or mastered audio.");
        }

        if (narration.SourceJobId != storyboardJobId)
        {
            throw new JobExecutionException(
                "render_narration_storyboard_mismatch",
                "The narration track belongs to a different storyboard.");
        }

        var narrationFile = ReadVerified(
            narration.Path,
            narration.ContentHash,
            "render_narration_unreadable",
            "render_narration_hash_mismatch");

        return new ResolvedAudio(
            narrationFile.AbsolutePath,
            RenderAudioSources.Narration,
            narration.Id,
            narrationFile.ContentHash,
            MasteredPath: null,
            MasteredContentHash: null);
    }

    private async Task<ResolvedSubtitle> ResolveSubtitleAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        GenerateStoryboardResult storyboard,
        CancellationToken cancellationToken)
    {
        var subtitle = await subtitles.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        if (subtitle is null || subtitle.SourceJobId != storyboardJobId)
        {
            return new ResolvedSubtitle(null, null, null, null);
        }

        var subtitleFile = ReadVerified(
            subtitle.Path,
            subtitle.ContentHash,
            "render_subtitle_unreadable",
            "render_subtitle_hash_mismatch");

        // Re-time the operator's own cues to the derived scene boundaries.
        // Only a one-cue-per-scene mapping is re-timed; any other shape keeps
        // the canonical timing as the deterministic fallback.
        IReadOnlyList<string>? cueTexts = null;
        var canonicalCues = SubtitleTimeline.ReadCueTexts(
            await File.ReadAllTextAsync(subtitleFile.AbsolutePath, cancellationToken));
        if (canonicalCues.Count == storyboard.Scenes.Count)
        {
            cueTexts = canonicalCues;
        }

        return new ResolvedSubtitle(
            subtitleFile.AbsolutePath,
            subtitle.Id,
            subtitleFile.ContentHash,
            cueTexts);
    }

    private async Task<GenerateStoryboardResult> FindStoryboardAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        CancellationToken cancellationToken)
    {
        var job = await jobs.FindByIdAsync(storyboardJobId, cancellationToken);
        if (job is null || job.ContentProjectId != contentProjectId)
        {
            throw new JobExecutionException(
                "render_storyboard_not_found",
                $"GenerateStoryboard job '{storyboardJobId}' does not exist for this content project.");
        }

        if (job.Type != JobType.GenerateStoryboard
            || job.Status != JobStatus.Succeeded
            || job.Result is null)
        {
            throw new JobExecutionException(
                "render_storyboard_invalid",
                "Rendering requires a completed GenerateStoryboard job result.");
        }

        try
        {
            return GenerateStoryboardResult.Deserialize(job.Result);
        }
        catch (JobExecutionException exception)
        {
            throw new JobExecutionException(
                "render_storyboard_invalid",
                "The storyboard result is not a valid structured storyboard.",
                exception);
        }
    }

    private static string BuildOutputPath(
        Guid contentProjectId,
        Guid storyboardJobId,
        string? variant) =>
        string.IsNullOrWhiteSpace(variant)
            ? $"renders/{contentProjectId:N}/{storyboardJobId:N}.mp4"
            : $"renders/{contentProjectId:N}/{storyboardJobId:N}-{variant}.mp4";

    private AssetFileInfo ReadVerified(
        string path,
        string expectedHash,
        string unreadableCode,
        string mismatchCode)
    {
        AssetFileInfo file;
        try
        {
            file = fileStore.Register(path);
        }
        catch (AssetCollectionException exception)
        {
            throw new JobExecutionException(unreadableCode, exception.Message, exception);
        }

        if (!string.Equals(
            file.ContentHash,
            expectedHash,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new JobExecutionException(
                mismatchCode,
                $"'{path}' does not match its recorded integrity hash.");
        }

        return file;
    }

    private sealed record ResolvedAudio(
        string AbsolutePath,
        string Source,
        Guid? NarrationTrackId,
        string? NarrationContentHash,
        string? MasteredPath,
        string? MasteredContentHash);

    private sealed record ResolvedSubtitle(
        string? Path,
        Guid? TrackId,
        string? ContentHash,
        IReadOnlyList<string>? CueTexts);
}
