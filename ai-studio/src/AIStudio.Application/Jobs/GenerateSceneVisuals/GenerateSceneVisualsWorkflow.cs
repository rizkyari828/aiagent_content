using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.GenerateSceneVisuals;

public sealed class GenerateSceneVisualsWorkflow(
    IApplicationDbContext dbContext,
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IProductionRecipeRegistry recipes,
    IIdentityAssetResolver identityAssetResolver,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private const int MaxArtDirectionLength = 400;

    public Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        CancellationToken cancellationToken) =>
        EnqueueAsync(contentProjectId, storyboardJobId, force: false, productionRecipe: null, artDirection: null, identityReferences: null, cancellationToken);

    /// <summary>
    /// <paramref name="force"/> is an explicit operator action: it regenerates every
    /// scene (including existing manual/generated assets) instead of reusing them.
    /// A normal re-run keeps reusing current-version assets.
    /// </summary>
    public Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        bool force,
        CancellationToken cancellationToken) =>
        EnqueueAsync(contentProjectId, storyboardJobId, force, productionRecipe: null, artDirection: null, identityReferences: null, cancellationToken);

    /// <summary>Identity-only selection; no production recipe or art-direction context.</summary>
    public Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        bool force,
        IReadOnlyList<AssetReference>? identityReferences,
        CancellationToken cancellationToken) =>
        EnqueueAsync(contentProjectId, storyboardJobId, force, productionRecipe: null, artDirection: null, identityReferences, cancellationToken);

    /// <summary>
    /// Materialization boundary. <paramref name="productionRecipe"/> is the explicit
    /// per-job recipe selection, <paramref name="artDirection"/> is optional narrative
    /// creative style, and <paramref name="identityReferences"/> are the explicit,
    /// caller-selected Bible references. They are resolved/normalized exactly once
    /// here, before the job is persisted: a floating identity version becomes a
    /// concrete pin and a recipe must name a registered exact version. The durable
    /// payload then carries only stable values, so execution and retries never
    /// re-resolve and cannot drift. Nothing is inferred from storyboard text.
    /// </summary>
    public async Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        bool force,
        ProductionRecipeReference? productionRecipe,
        string? artDirection,
        IReadOnlyList<AssetReference>? identityReferences,
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
            jobs,
            cancellationToken);

        var recipe = MaterializeProductionRecipe(productionRecipe);
        var normalizedArtDirection = NormalizeArtDirection(artDirection);
        var identityPins = MaterializeIdentityReferences(identityReferences);

        var payload = JsonSerializer.Serialize(
            new GenerateSceneVisualsJobPayload(contentProjectId, storyboardJobId, force)
            {
                ProductionRecipe = recipe,
                ArtDirection = normalizedArtDirection,
                IdentityReferences = identityPins.Count == 0 ? null : identityPins
            },
            JsonOptions);

        var job = Job.Create(
            contentProjectId,
            JobType.GenerateSceneVisuals,
            ComputeInputVersionHash(payload, storyboard),
            payload,
            timeProvider.GetUtcNow());

        dbContext.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return job.Id;
    }

    /// <summary>
    /// Shared by the workflow and the handler so enqueue and execution gate on the
    /// same completed storyboard.
    /// </summary>
    internal static async Task<GenerateStoryboardResult> RequireStoryboardAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        IJobReader jobs,
        CancellationToken cancellationToken)
    {
        var job = await jobs.FindByIdAsync(storyboardJobId, cancellationToken);
        if (job is null || job.ContentProjectId != contentProjectId)
        {
            throw Error(
                "visual_storyboard_not_found",
                $"GenerateStoryboard job '{storyboardJobId}' does not exist for this content project.");
        }

        if (job.Type != JobType.GenerateStoryboard
            || job.Status != JobStatus.Succeeded
            || job.Result is null)
        {
            throw Error(
                "visual_storyboard_invalid",
                "Visual generation requires a completed GenerateStoryboard job result.");
        }

        try
        {
            return GenerateStoryboardResult.Deserialize(job.Result);
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "visual_storyboard_invalid",
                "The storyboard result is not a valid structured storyboard.",
                exception);
        }
    }

    /// <summary>
    /// Normalizes the optional narrative art direction. Blank means "none"; an
    /// over-long value or one containing control characters is rejected before the
    /// job is created so it can never reach the image provider.
    /// </summary>
    private static string? NormalizeArtDirection(string? artDirection)
    {
        if (string.IsNullOrWhiteSpace(artDirection))
        {
            return null;
        }

        var trimmed = artDirection.Trim();
        if (trimmed.Length > MaxArtDirectionLength || trimmed.Any(char.IsControl))
        {
            throw Error(
                "visual_art_direction_invalid",
                $"Art direction must be at most {MaxArtDirectionLength} characters and contain no control characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// Resolves the explicitly selected recipe to a registered exact (id, version).
    /// "Latest" is never consulted: an unknown id or version fails before the job is
    /// created, so the persisted payload can only ever replay a known recipe.
    /// </summary>
    private ProductionRecipeReference? MaterializeProductionRecipe(
        ProductionRecipeReference? selection)
    {
        if (selection is null)
        {
            return null;
        }

        if (!recipes.TryGet(selection.Id, selection.Version, out _))
        {
            throw Error(
                "visual_production_recipe_not_found",
                $"Production recipe '{selection.Id}' v{selection.Version.Value} is not registered.");
        }

        return new ProductionRecipeReference
        {
            Id = selection.Id,
            Version = selection.Version
        };
    }

    /// <summary>
    /// Resolves each caller-selected reference to one concrete Approved pin. An
    /// explicit version resolves that exact asset; a null version resolves the
    /// latest Approved asset once. Draft/missing references fail clearly and the
    /// job is not created; more than one reference is unsupported in v1.
    /// </summary>
    private IReadOnlyList<PinnedIdentityAsset> MaterializeIdentityReferences(
        IReadOnlyList<AssetReference>? identityReferences)
    {
        if (identityReferences is null || identityReferences.Count == 0)
        {
            return [];
        }

        if (identityReferences.Count > 1)
        {
            throw Error(
                "visual_identity_reference_count_unsupported",
                "GenerateSceneVisuals supports at most one identity reference in v1.");
        }

        var pins = new List<PinnedIdentityAsset>(identityReferences.Count);
        foreach (var reference in identityReferences)
        {
            var resolution = identityAssetResolver.Resolve(reference);
            if (!resolution.IsSuccess || resolution.Value is null)
            {
                var issue = resolution.Issues.FirstOrDefault();
                throw Error(
                    "visual_identity_reference_unresolved",
                    $"Identity reference '{reference.AssetId}' could not be resolved to an approved version"
                    + (issue is null ? "." : $": {issue.Message}"));
            }

            pins.Add(resolution.Value);
        }

        return pins;
    }

    private static string ComputeInputVersionHash(
        string payload,
        GenerateStoryboardResult storyboard)
    {
        var builder = new StringBuilder(payload)
            .Append('\n')
            .Append(storyboard.Serialize())
            .Append('\n')
            .Append(SceneVisualPlanner.PlannerVersion);

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

    private static SceneVisualGenerationException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
