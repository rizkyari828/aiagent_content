using System.Security.Cryptography;
using System.Text;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Narration;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.GenerateSceneVisuals;

/// <summary>
/// Durable visual generation. It runs the deterministic director/router over the
/// completed storyboard, then renders each scene through the routed engine:
/// Blender ThreeD, Manim, animated SVG motion-graphics, FLUX image with motion
/// treatment, or the deterministic SVG still. An asset produced by the current
/// visual-direction version is reused; an older generated asset is regenerated in
/// place; a manually registered asset is never overwritten. Per-scene routing and
/// generated/reused/fallback status are recorded in the job result.
/// </summary>
public sealed class GenerateSceneVisualsJobHandler(
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IProductionRecipeRegistry recipes,
    IAssetRepository assets,
    INarrationRepository narrations,
    IAssetFileStore fileStore,
    AudioProductionWorkspace audioWorkspace,
    ISceneVisualRenderer svgRenderer,
    IManimSceneRenderer manimRenderer,
    IImageGenerationProvider imageProvider,
    IThreeDRenderingProvider threeDRenderer,
    IMediaInspector mediaInspector,
    TimeProvider timeProvider) : IJobHandler
{
    private const string ImageOverlayLabel = "AI LOKAL · OFFLINE";

    public bool CanHandle(JobType type) => type == JobType.GenerateSceneVisuals;

    public async Task<string> ExecuteAsync(
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        if (job.Type != JobType.GenerateSceneVisuals)
        {
            throw Error(
                "visual_wrong_job_type",
                $"GenerateSceneVisuals handler cannot execute job type {job.Type}.");
        }

        var payload = GenerateSceneVisualsJobPayload.Deserialize(job.Payload);
        if (payload.ContentProjectId != job.ContentProjectId)
        {
            throw Error(
                "visual_invalid_payload",
                "GenerateSceneVisuals payload contentProjectId does not match the claimed job.");
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

        var animated = Enumerable.Repeat(true, storyboard.Scenes.Count).ToArray();
        var timing = await ResolveTimingAsync(
            job.ContentProjectId,
            payload.StoryboardJobId,
            storyboard,
            animated,
            cancellationToken);

        var routingProfile = ResolveRoutingProfile(payload.ProductionRecipe);
        var narrativeImage = routingProfile.NarrativeEngine == SceneVisualEngine.AiImage;

        var plans = SceneVisualPlanner.PlanAll(
            storyboard,
            timing.Durations,
            manimRenderer.IsEnabled,
            imageProvider.IsEnabled,
            threeDRenderer.IsEnabled,
            timing.Windows,
            routingProfile);

        // Concrete pins persisted at materialization. Execution never resolves a
        // latest version and never reads the Bible/registry to pick an asset.
        var identityReferences = payload.IdentityReferences ?? [];

        var generated = new List<GeneratedSceneVisual>(plans.Count);
        var routing = new List<SceneVisualRouting>(plans.Count);

        for (var sceneIndex = 0; sceneIndex < plans.Count; sceneIndex++)
        {
            var plan = plans[sceneIndex];
            var expectedCreator = SceneVisualFingerprint.Creator(plan);
            var existing = await assets.FindBySceneAsync(
                job.ContentProjectId,
                sceneIndex,
                cancellationToken);

            if (!payload.Force
                && existing is not null
                && (!SceneAssetProvenance.IsGeneratedGraphic(existing.Source)
                    || string.Equals(existing.Creator, expectedCreator, StringComparison.Ordinal)))
            {
                // Manual assets always win; a current-version generated asset is
                // reused only when its per-scene plan fingerprint is unchanged.
                var engine = SceneVisualFingerprint.EngineFromCreator(existing.Creator);
                routing.Add(Route(plan, string.IsNullOrEmpty(engine) ? plan.Engine.ToString() : engine, "reused"));
                continue;
            }

            var (relativePath, bytes, assetType) = await GenerateAsync(
                plan,
                sceneIndex,
                job.ContentProjectId,
                payload.StoryboardJobId,
                identityReferences,
                narrativeImage,
                payload.ArtDirection,
                cancellationToken);
            var file = await WriteAsync(relativePath, bytes, cancellationToken);

            if (existing is null)
            {
                var asset = SceneAsset.Create(
                    job.ContentProjectId,
                    payload.StoryboardJobId,
                    sceneIndex,
                    assetType,
                    file.RelativePath,
                    file.ByteSize,
                    file.ContentHash,
                    AssetOrigin.Local,
                    SceneAssetProvenance.CurrentGeneratedVisualSource,
                    expectedCreator,
                    license: null,
                    retrievedAt: null,
                    timeProvider.GetUtcNow());
                assets.Add(asset);
            }
            else
            {
                // Older generated asset: replace in place under the same identity.
                existing.Replace(
                    assetType,
                    file.RelativePath,
                    file.ByteSize,
                    file.ContentHash,
                    SceneAssetProvenance.CurrentGeneratedVisualSource,
                    expectedCreator);
            }

            await assets.SaveChangesAsync(cancellationToken);

            var status = plan.IsFallback ? "fallback" : "generated";
            generated.Add(new GeneratedSceneVisual(
                sceneIndex,
                plan.Engine.ToString(),
                TemplateName(plan),
                file.RelativePath,
                file.ByteSize,
                file.ContentHash,
                plan.IntendedEngine.ToString(),
                status,
                plan.Direction?.Intent.ToString() ?? string.Empty));
            routing.Add(Route(plan, plan.Engine.ToString(), status));
        }

        var result = new GenerateSceneVisualsResult(
            payload.StoryboardJobId,
            plans.Count,
            generated.Count,
            plans.Count - generated.Count,
            generated,
            routing);

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
            throw Error(exception.ErrorCode, exception.Message, exception);
        }
    }

    /// <summary>
    /// Derives the visual routing context from the persisted recipe identity. The
    /// exact (id, version) is re-read from the trusted registry, never "latest", so
    /// a replay of the same payload routes identically. No recipe (or an unchanged
    /// recipe) keeps the historical intent-only routing.
    /// </summary>
    private SceneVisualRoutingProfile ResolveRoutingProfile(
        ProductionRecipeReference? reference)
    {
        if (reference is null)
        {
            return SceneVisualRoutingProfile.Default;
        }

        if (!recipes.TryGet(reference.Id, reference.Version, out var recipe))
        {
            throw Error(
                "visual_production_recipe_not_found",
                $"Production recipe '{reference.Id}' v{reference.Version.Value} is not registered.");
        }

        return SceneVisualRoutingProfile.FromRecipe(recipe);
    }

    /// <summary>
    /// Prefers the speech-driven per-scene timing recorded by GenerateAudio (scene
    /// length and narration window). When no audio workspace exists it falls back to
    /// the shared narration-aware allocation; when narration is absent the
    /// deterministic animation floor fits.
    /// </summary>
    private async Task<(double[] Durations, IReadOnlyList<SceneNarrationWindow>? Windows)> ResolveTimingAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        GenerateStoryboardResult storyboard,
        bool[] animated,
        CancellationToken cancellationToken)
    {
        var fallback = storyboard.Scenes
            .Select(_ => SceneVisualPlanner.AnimationDurationSeconds)
            .ToArray();

        var manifest = await audioWorkspace.TryReadManifestAsync(
            contentProjectId,
            storyboardJobId,
            cancellationToken);
        if (manifest?.Scenes is { Count: > 0 } scenes
            && scenes.Count == storyboard.Scenes.Count)
        {
            var ordered = scenes.OrderBy(scene => scene.SceneIndex).ToArray();
            var durations = ordered
                .Select(scene => scene.VisualDurationSeconds)
                .ToArray();
            var windows = ordered
                .Select(scene => new SceneNarrationWindow(
                    scene.SceneIndex,
                    scene.NarrationStartSeconds - scene.VisualStartSeconds,
                    scene.NarrationDurationSeconds))
                .ToArray();
            return (durations, windows);
        }

        try
        {
            var narration = await narrations.FindByProjectIdAsync(
                contentProjectId,
                cancellationToken);
            if (narration is null || narration.SourceJobId != storyboardJobId)
            {
                return (fallback, null);
            }

            var narrationFile = fileStore.Register(narration.Path);
            var inspection = await mediaInspector.InspectAsync(
                narrationFile.AbsolutePath,
                cancellationToken);
            if (inspection.DurationSeconds <= 0)
            {
                return (fallback, null);
            }

            return (SceneTiming.AllocateForScenes(
                storyboard.Scenes.Select(scene => scene.Heading).ToArray(),
                storyboard.Scenes.Select(scene => scene.Visual).ToArray(),
                animated,
                inspection.DurationSeconds), null);
        }
        catch (AssetCollectionException)
        {
            return (fallback, null);
        }
        catch (ProcessExecutionException)
        {
            return (fallback, null);
        }
    }

    private async Task<(string RelativePath, byte[] Bytes, AssetType Type)> GenerateAsync(
        SceneVisualPlan plan,
        int sceneIndex,
        Guid contentProjectId,
        Guid storyboardJobId,
        IReadOnlyList<PinnedIdentityAsset> identityReferences,
        bool narrativeImage,
        string? artDirection,
        CancellationToken cancellationToken)
    {
        var duration = plan.Direction?.DurationSeconds
            ?? SceneVisualPlanner.AnimationDurationSeconds;
        var animated = plan.Engine != SceneVisualEngine.SvgStill;
        var extension = animated ? "mp4" : "png";
        var relativePath =
            $"visuals/{contentProjectId:N}/{storyboardJobId:N}/scene_{sceneIndex}.{extension}";

        try
        {
            var bytes = plan.Engine switch
            {
                SceneVisualEngine.ManimAnimation => await RenderManimAsync(plan, cancellationToken),
                SceneVisualEngine.ThreeD => await RenderThreeDAsync(
                    plan,
                    storyboardJobId,
                    sceneIndex,
                    duration,
                    cancellationToken),
                SceneVisualEngine.AiImage => await RenderAiImageAsync(
                    plan,
                    storyboardJobId,
                    sceneIndex,
                    duration,
                    identityReferences,
                    narrativeImage,
                    artDirection,
                    cancellationToken),
                SceneVisualEngine.AnimatedSvg => await svgRenderer.RenderAnimationAsync(
                    plan.Brief,
                    plan.Direction?.Choreography ?? SceneChoreography.Empty,
                    duration,
                    cancellationToken),
                _ => await svgRenderer.RenderPngAsync(plan.Brief, cancellationToken)
            };

            return (relativePath, bytes, animated ? AssetType.Video : AssetType.Image);
        }
        catch (RenderVideoException exception)
        {
            throw Error(exception.ErrorCode, exception.Message, exception);
        }
    }

    private async Task<byte[]> RenderManimAsync(
        SceneVisualPlan plan,
        CancellationToken cancellationToken)
    {
        var animation = plan.Animation
            ?? throw Error(
                "visual_plan_invalid",
                $"Scene {plan.Brief.SceneIndex} selected Manim without parameters.");

        return await manimRenderer.RenderAsync(plan.Template, animation, cancellationToken);
    }

    private async Task<byte[]> RenderThreeDAsync(
        SceneVisualPlan plan,
        Guid storyboardJobId,
        int sceneIndex,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        if (plan.ThreeDTemplate == SceneThreeDTemplate.None)
        {
            throw Error(
                "visual_plan_invalid",
                $"Scene {sceneIndex} selected 3D without a template.");
        }

        return await threeDRenderer.RenderAsync(
            new ThreeDRenderRequest(
                plan.ThreeDTemplate,
                plan.Brief.Palette,
                durationSeconds,
                DeriveSeed(storyboardJobId, sceneIndex)),
            cancellationToken);
    }

    private async Task<byte[]> RenderAiImageAsync(
        SceneVisualPlan plan,
        Guid storyboardJobId,
        int sceneIndex,
        double durationSeconds,
        IReadOnlyList<PinnedIdentityAsset> identityReferences,
        bool narrativeImage,
        string? artDirection,
        CancellationToken cancellationToken)
    {
        ImageGenerationRequest request;
        try
        {
            // The exact persisted pins flow through unchanged. Beyond one reference
            // is unsupported by the FLUX provider in v1 and must fail clearly rather
            // than silently choosing the first.
            request = new ImageGenerationRequest(
                SceneImagePrompt.Build(plan.Brief, narrativeImage, artDirection),
                DeriveSeed(storyboardJobId, sceneIndex),
                identityReferences);
        }
        catch (NotSupportedException exception)
        {
            throw Error(
                "visual_identity_reference_count_unsupported",
                exception.Message,
                exception);
        }
        catch (ArgumentException exception)
        {
            throw Error(
                "visual_identity_reference_invalid",
                exception.Message,
                exception);
        }

        var image = await imageProvider.GenerateAsync(request, cancellationToken);

        // A still alone is not a scene: apply the repository-owned motion treatment.
        // A narrative scene carries no technical badge, so only the slow push remains.
        return await svgRenderer.RenderImageMotionAsync(
            image,
            narrativeImage ? string.Empty : ImageOverlayLabel,
            plan.Brief.Palette,
            durationSeconds,
            cancellationToken);
    }

    private async Task<AssetFileInfo> WriteAsync(
        string relativePath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        try
        {
            return await fileStore.WriteAsync(relativePath, bytes, cancellationToken);
        }
        catch (AssetCollectionException exception)
        {
            throw Error("visual_asset_write_failed", exception.Message, exception);
        }
    }

    private static SceneVisualRouting Route(
        SceneVisualPlan plan,
        string engine,
        string status) =>
        new(
            plan.Brief.SceneIndex,
            plan.IntendedEngine.ToString(),
            engine,
            status,
            TemplateName(plan));

    /// <summary>
    /// Engine-appropriate template name for the recorded result: the Manim template
    /// for animation, the Blender template for 3D, and <c>None</c> otherwise.
    /// </summary>
    private static string TemplateName(SceneVisualPlan plan) =>
        plan.Engine switch
        {
            SceneVisualEngine.ManimAnimation => plan.Template.ToString(),
            SceneVisualEngine.ThreeD => plan.ThreeDTemplate.ToString(),
            _ => SceneAnimationTemplate.None.ToString()
        };

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

    private static JobExecutionException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
