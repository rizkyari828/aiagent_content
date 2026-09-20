using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Narration;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.RenderVideo;

public sealed class RenderVideoWorkflow(
    IApplicationDbContext dbContext,
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IAssetRepository assets,
    INarrationRepository narrations,
    AudioProductionWorkspace audioWorkspace,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(storyboardJobId, nameof(storyboardJobId));

        var project = await contentProjects.FindByIdAsync(
            contentProjectId,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        var storyboard = await RequireStoryboardAsync(
            contentProjectId,
            storyboardJobId,
            cancellationToken);

        var registered = await assets.ListByProjectAsync(
            contentProjectId,
            cancellationToken);
        var byScene = registered.ToDictionary(asset => asset.SceneIndex);

        for (var sceneIndex = 0; sceneIndex < storyboard.Scenes.Count; sceneIndex++)
        {
            if (!byScene.TryGetValue(sceneIndex, out var asset))
            {
                throw Error(
                    "render_assets_incomplete",
                    $"Scene {sceneIndex} has no registered asset.");
            }

            if (asset.SourceJobId != storyboardJobId)
            {
                throw Error(
                    "render_asset_storyboard_mismatch",
                    $"Scene {sceneIndex} asset belongs to a different storyboard.");
            }
        }

        var narration = await narrations.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);

        var manifest = await audioWorkspace.TryReadManifestAsync(
            contentProjectId,
            storyboardJobId,
            cancellationToken);
        var master = await audioWorkspace.TryResolveStageAsync(
            manifest,
            AudioProductionWorkspace.MasterStage,
            cancellationToken);

        string audioHash;
        if (master is not null)
        {
            // The mastered audio from GenerateAudio supersedes the narration track.
            audioHash = master.ContentHash;
        }
        else
        {
            if (narration is null)
            {
                throw Error(
                    "render_audio_missing",
                    $"Content project '{contentProjectId}' does not have narration or mastered audio.");
            }

            if (narration.SourceJobId != storyboardJobId)
            {
                throw Error(
                    "render_narration_storyboard_mismatch",
                    "The narration track belongs to a different storyboard.");
            }

            audioHash = narration.ContentHash;
        }

        var payload = JsonSerializer.Serialize(
            new RenderVideoJobPayload(contentProjectId, storyboardJobId),
            JsonOptions);

        var job = Job.Create(
            contentProjectId,
            JobType.RenderVideo,
            ComputeInputVersionHash(payload, storyboard, byScene.Values, audioHash),
            payload,
            timeProvider.GetUtcNow());

        dbContext.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return job.Id;
    }

    private async Task<GenerateStoryboardResult> RequireStoryboardAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        CancellationToken cancellationToken)
    {
        var job = await jobs.FindByIdAsync(storyboardJobId, cancellationToken);
        if (job is null || job.ContentProjectId != contentProjectId)
        {
            throw Error(
                "render_storyboard_not_found",
                $"GenerateStoryboard job '{storyboardJobId}' does not exist for this content project.");
        }

        if (job.Type != JobType.GenerateStoryboard
            || job.Status != JobStatus.Succeeded
            || job.Result is null)
        {
            throw Error(
                "render_storyboard_invalid",
                "Rendering requires a completed GenerateStoryboard job result.");
        }

        try
        {
            return GenerateStoryboardResult.Deserialize(job.Result);
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "render_storyboard_invalid",
                "The storyboard result is not a valid structured storyboard.",
                exception);
        }
    }

    private static string ComputeInputVersionHash(
        string payload,
        GenerateStoryboardResult storyboard,
        IEnumerable<Domain.Assets.SceneAsset> assets,
        string audioHash)
    {
        var builder = new StringBuilder(payload)
            .Append('\n')
            .Append(storyboard.Serialize());

        foreach (var asset in assets.OrderBy(asset => asset.SceneIndex))
        {
            builder.Append('\n')
                .Append(asset.SceneIndex)
                .Append(':')
                .Append(asset.ContentHash);
        }

        builder.Append('\n').Append(audioHash);

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }

    private static RenderVideoException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
