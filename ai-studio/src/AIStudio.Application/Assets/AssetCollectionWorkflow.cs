using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Assets;

public sealed class AssetCollectionWorkflow(
    IAssetRepository assets,
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IAssetFileStore fileStore,
    TimeProvider timeProvider)
{
    public async Task<SceneAssetSnapshot?> RegisterAsync(
        Guid contentProjectId,
        Guid jobId,
        RegisterSceneAsset request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(jobId, nameof(jobId));

        var project = await contentProjects.FindByIdAsync(
            contentProjectId,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        var storyboard = await FindStoryboardAsync(
            contentProjectId,
            jobId,
            cancellationToken);

        if (request.SceneIndex < 0 || request.SceneIndex >= storyboard.Scenes.Count)
        {
            throw Error(
                "asset_scene_not_found",
                $"Scene index {request.SceneIndex} does not exist in the storyboard.");
        }

        var existing = await assets.FindBySceneAsync(
            contentProjectId,
            request.SceneIndex,
            cancellationToken);
        if (existing is not null)
        {
            throw Error(
                "asset_scene_conflict",
                $"Scene {request.SceneIndex} already has a registered asset.");
        }

        if (request.Origin == AssetOrigin.External
            && (string.IsNullOrWhiteSpace(request.Source)
                || string.IsNullOrWhiteSpace(request.License)))
        {
            throw Error(
                "asset_provenance_required",
                "Externally sourced assets require a source and license.");
        }

        var file = fileStore.Register(request.Path);

        var asset = SceneAsset.Create(
            contentProjectId,
            jobId,
            request.SceneIndex,
            request.Type,
            file.RelativePath,
            file.ByteSize,
            file.ContentHash,
            request.Origin,
            request.Source,
            request.Creator,
            request.License,
            request.RetrievedAt,
            timeProvider.GetUtcNow());

        assets.Add(asset);
        await assets.SaveChangesAsync(cancellationToken);
        return ToSnapshot(asset);
    }

    public async Task<IReadOnlyList<SceneAssetSnapshot>> ListAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        var items = await assets.ListByProjectAsync(contentProjectId, cancellationToken);
        return items.Select(ToSnapshot).ToArray();
    }

    private async Task<GenerateStoryboardResult> FindStoryboardAsync(
        Guid contentProjectId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await jobs.FindByIdAsync(jobId, cancellationToken);
        if (job is null || job.ContentProjectId != contentProjectId)
        {
            throw Error(
                "asset_storyboard_not_found",
                $"GenerateStoryboard job '{jobId}' does not exist for this content project.");
        }

        if (job.Type != JobType.GenerateStoryboard
            || job.Status != JobStatus.Succeeded
            || job.Result is null)
        {
            throw Error(
                "asset_storyboard_invalid",
                "Assets require a completed GenerateStoryboard job result.");
        }

        try
        {
            return GenerateStoryboardResult.Deserialize(job.Result);
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "asset_storyboard_invalid",
                "The storyboard result is not a valid structured storyboard.",
                exception);
        }
    }

    private static SceneAssetSnapshot ToSnapshot(SceneAsset asset) =>
        new(
            asset.Id,
            asset.ContentProjectId,
            asset.SourceJobId,
            asset.SceneIndex,
            asset.Type,
            asset.Path,
            asset.ByteSize,
            asset.ContentHash,
            asset.Origin,
            asset.Source,
            asset.Creator,
            asset.License,
            asset.RetrievedAt,
            asset.CreatedAt);

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }

    private static AssetCollectionException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}

public sealed record RegisterSceneAsset(
    int SceneIndex,
    string Path,
    AssetType Type,
    AssetOrigin Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt);
