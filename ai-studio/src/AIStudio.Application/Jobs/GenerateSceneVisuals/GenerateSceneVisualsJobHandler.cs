using System.Security.Cryptography;
using System.Text;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Narration;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.GenerateSceneVisuals;

/// <summary>
/// Durable visual generation: maps the completed storyboard into structured visual
/// plans, renders each scene through the selected engine, persists the bytes under
/// the approved asset root, and registers the result as a canonical
/// <see cref="SceneAsset"/>. Scenes that already have an asset are skipped, so a
/// re-run is idempotent and manually registered or canonical assets are preserved.
/// Animated clips use the same derived <see cref="SceneTiming"/> duration the
/// renderer later uses; when narration is unavailable the deterministic animation
/// floor is kept. Still scenes use the deterministic SVG engine unless the optional
/// local AI image provider is enabled.
/// </summary>
public sealed class GenerateSceneVisualsJobHandler(
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IAssetRepository assets,
    INarrationRepository narrations,
    IAssetFileStore fileStore,
    ISceneVisualRenderer svgRenderer,
    IManimSceneRenderer manimRenderer,
    IImageGenerationProvider imageProvider,
    IMediaInspector mediaInspector,
    TimeProvider timeProvider) : IJobHandler
{
    public bool CanHandle(JobType type) => type == JobType.GenerateSceneVisuals;

    public async Task<string> ExecuteAsync(
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        if (job.Type != JobType.GenerateSceneVisuals)
        {
            throw new JobExecutionException(
                "visual_wrong_job_type",
                $"GenerateSceneVisuals handler cannot execute job type {job.Type}.");
        }

        var payload = GenerateSceneVisualsJobPayload.Deserialize(job.Payload);
        if (payload.ContentProjectId != job.ContentProjectId)
        {
            throw new JobExecutionException(
                "visual_invalid_payload",
                "GenerateSceneVisuals payload contentProjectId does not match the claimed job.");
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

        var storyboard = await LoadStoryboardAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            cancellationToken);

        var plans = SceneVisualPlanner.PlanAll(
            storyboard,
            manimRenderer.IsEnabled,
            imageProvider.IsEnabled);
        var animationDurations = await TryResolveAnimationDurationsAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            storyboard,
            plans,
            cancellationToken);
        var registered = await assets.ListByProjectAsync(
            job.ContentProjectId,
            cancellationToken);
        var registeredScenes = registered
            .Select(asset => asset.SceneIndex)
            .ToHashSet();

        var generated = new List<GeneratedSceneVisual>(plans.Count);

        for (var sceneIndex = 0; sceneIndex < plans.Count; sceneIndex++)
        {
            if (registeredScenes.Contains(sceneIndex))
            {
                // Existing canonical/manual asset wins; never overwrite it.
                continue;
            }

            var plan = plans[sceneIndex];
            var (relativePath, bytes, assetType) = await GenerateAsync(
                plan,
                sceneIndex,
                job.ContentProjectId,
                payload.StoryboardJobId,
                animationDurations?[sceneIndex],
                cancellationToken);

            AssetFileInfo file;
            try
            {
                file = await fileStore.WriteAsync(relativePath, bytes, cancellationToken);
            }
            catch (AssetCollectionException exception)
            {
                throw new JobExecutionException(
                    "visual_asset_write_failed",
                    exception.Message,
                    exception);
            }

            var asset = SceneAsset.Create(
                job.ContentProjectId,
                payload.StoryboardJobId,
                sceneIndex,
                assetType,
                file.RelativePath,
                file.ByteSize,
                file.ContentHash,
                AssetOrigin.Local,
                SceneAssetProvenance.GeneratedVisualSource,
                plan.Engine.ToString(),
                license: null,
                retrievedAt: null,
                timeProvider.GetUtcNow());

            assets.Add(asset);
            await assets.SaveChangesAsync(cancellationToken);

            generated.Add(new GeneratedSceneVisual(
                sceneIndex,
                plan.Engine.ToString(),
                plan.Template.ToString(),
                file.RelativePath,
                file.ByteSize,
                file.ContentHash));
        }

        var result = new GenerateSceneVisualsResult(
            payload.StoryboardJobId,
            plans.Count,
            generated.Count,
            plans.Count - generated.Count,
            generated);

        return result.Serialize();
    }

    private async Task<GenerateStoryboardResult> LoadStoryboardAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GenerateSceneVisualsWorkflow.RequireStoryboardAsync(
                contentProjectId,
                storyboardJobId,
                jobs,
                cancellationToken);
        }
        catch (SceneVisualGenerationException exception)
        {
            // The workflow vocabulary is enqueue-facing; the handler surfaces the
            // same stable code through the job execution boundary.
            throw new JobExecutionException(
                exception.ErrorCode,
                exception.Message,
                exception);
        }
    }

    /// <summary>
    /// Derives the animation clip duration from the same narration-aware allocation
    /// the renderer uses. Narration is optional here: if it is absent or unreadable,
    /// the deterministic animation floor remains the fallback.
    /// </summary>
    private async Task<double[]?> TryResolveAnimationDurationsAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        GenerateStoryboardResult storyboard,
        IReadOnlyList<SceneVisualPlan> plans,
        CancellationToken cancellationToken)
    {
        try
        {
            var narration = await narrations.FindByProjectIdAsync(
                contentProjectId,
                cancellationToken);
            if (narration is null || narration.SourceJobId != storyboardJobId)
            {
                return null;
            }

            var narrationFile = fileStore.Register(narration.Path);
            var inspection = await mediaInspector.InspectAsync(
                narrationFile.AbsolutePath,
                cancellationToken);
            if (inspection.DurationSeconds <= 0)
            {
                return null;
            }

            return SceneTiming.AllocateForScenes(
                storyboard.Scenes.Select(scene => scene.Heading).ToArray(),
                storyboard.Scenes.Select(scene => scene.Visual).ToArray(),
                plans.Select(plan => plan.Engine == SceneVisualEngine.ManimAnimation).ToArray(),
                inspection.DurationSeconds);
        }
        catch (AssetCollectionException)
        {
            return null;
        }
        catch (ProcessExecutionException)
        {
            return null;
        }
    }

    private async Task<(string RelativePath, byte[] Bytes, AssetType Type)> GenerateAsync(
        SceneVisualPlan plan,
        int sceneIndex,
        Guid contentProjectId,
        Guid storyboardJobId,
        double? animationDurationSeconds,
        CancellationToken cancellationToken)
    {
        var animated = plan.Engine == SceneVisualEngine.ManimAnimation;
        var aiImage = plan.Engine == SceneVisualEngine.AiImage;
        var extension = animated ? "mp4" : "png";
        var relativePath =
            $"visuals/{contentProjectId:N}/{storyboardJobId:N}/scene_{sceneIndex}.{extension}";

        try
        {
            byte[] bytes;
            if (animated)
            {
                var animation = plan.Animation
                    ?? throw new JobExecutionException(
                        "visual_plan_invalid",
                        $"Scene {sceneIndex} selected animation without parameters.");
                if (animationDurationSeconds is > 0)
                {
                    animation = animation with { DurationSeconds = animationDurationSeconds.Value };
                }

                bytes = await manimRenderer.RenderAsync(
                    plan.Template,
                    animation,
                    cancellationToken);
            }
            else if (aiImage)
            {
                bytes = await imageProvider.GenerateAsync(
                    new ImageGenerationRequest(
                        SceneImagePrompt.Build(plan.Brief),
                        DeriveSeed(storyboardJobId, sceneIndex)),
                    cancellationToken);
            }
            else
            {
                bytes = await svgRenderer.RenderPngAsync(plan.Brief, cancellationToken);
            }

            return (
                relativePath,
                bytes,
                animated ? AssetType.Video : AssetType.Image);
        }
        catch (RenderVideoException exception)
        {
            throw new JobExecutionException(
                exception.ErrorCode,
                exception.Message,
                exception);
        }
    }

    /// <summary>
    /// Stable per-scene seed so a re-run of the same storyboard reproduces the same
    /// image. Derived from the storyboard job id and scene index, never randomized.
    /// </summary>
    private static long DeriveSeed(Guid storyboardJobId, int sceneIndex)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(
            Encoding.UTF8.GetBytes($"{storyboardJobId:N}:{sceneIndex}"),
            hash);
        return (long)(BitConverter.ToUInt64(hash) & long.MaxValue);
    }
}
