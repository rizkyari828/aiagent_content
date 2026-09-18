using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Narration;
using AIStudio.Application.Rendering;
using AIStudio.Application.Subtitles;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.RenderVideo;

public sealed class RenderVideoJobHandler(
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IAssetRepository assets,
    INarrationRepository narrations,
    ISubtitleRepository subtitles,
    IAssetFileStore fileStore,
    IVideoRenderer renderer) : IJobHandler
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

        var registered = await assets.ListByProjectAsync(
            job.ContentProjectId,
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

            if (asset.SourceJobId != payload.StoryboardJobId)
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
            sceneInputs.Add(new SceneMediaInput(assetFile.AbsolutePath, asset.Type));
        }

        var narration = await narrations.FindByProjectIdAsync(
            job.ContentProjectId,
            cancellationToken);
        if (narration is null)
        {
            throw new JobExecutionException(
                "render_narration_not_found",
                $"Content project '{job.ContentProjectId}' does not have a narration track.");
        }

        if (narration.SourceJobId != payload.StoryboardJobId)
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

        string? subtitlePath = null;
        Guid? subtitleTrackId = null;
        string? subtitleContentHash = null;

        var subtitle = await subtitles.FindByProjectIdAsync(
            job.ContentProjectId,
            cancellationToken);
        if (subtitle is not null && subtitle.SourceJobId == payload.StoryboardJobId)
        {
            var subtitleFile = ReadVerified(
                subtitle.Path,
                subtitle.ContentHash,
                "render_subtitle_unreadable",
                "render_subtitle_hash_mismatch");
            subtitlePath = subtitleFile.AbsolutePath;
            subtitleTrackId = subtitle.Id;
            subtitleContentHash = subtitleFile.ContentHash;
        }

        var relativeOutput = $"renders/{job.ContentProjectId:N}/{payload.StoryboardJobId:N}.mp4";

        VideoRenderOutput output;
        try
        {
            output = await renderer.RenderAsync(
                new VideoRenderRequest(
                    sceneInputs,
                    narrationFile.AbsolutePath,
                    relativeOutput,
                    subtitlePath),
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
            narration.Id,
            sceneInputs.Count,
            narrationFile.ContentHash,
            subtitleTrackId,
            subtitleContentHash,
            subtitlePath is not null);

        return result.Serialize();
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
}
