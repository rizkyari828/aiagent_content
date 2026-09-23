using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.ProductionRecipes;
using Microsoft.AspNetCore.Mvc;

namespace AIStudio.Api.Endpoints;

public static class VisualEndpoints
{
    public static IEndpointRouteBuilder MapVisualApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost(
                "/content-projects/{contentProjectId}/storyboard-jobs/{storyboardJobId}/visual-jobs",
                EnqueueVisualsAsync)
            .WithName("EnqueueSceneVisuals")
            .Produces<EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        return endpoints;
    }

    internal static async Task<IResult> EnqueueVisualsAsync(
        string contentProjectId,
        string storyboardJobId,
        bool? force,
        [FromBody] EnqueueVisualsRequest? request,
        GenerateSceneVisualsWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var parsedProjectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        if (!TryParseIdentifier(storyboardJobId, out var parsedStoryboardJobId))
        {
            return InvalidIdentifier("storyboardJobId");
        }

        // Optional explicit production-format selection. The caller names one exact
        // recipe version; this endpoint only validates the shape and never resolves a
        // "latest" recipe. Materialization/registration check stays in the workflow.
        ProductionRecipeReference? productionRecipe = null;
        if (request?.ProductionRecipe is { } recipeSelection)
        {
            if (!ProductionRecipeId.TryParse(recipeSelection.Id, out var recipeId))
            {
                return Validation(
                    "productionRecipe.id",
                    "A valid lowercase production recipe id is required.");
            }

            if (recipeSelection.Version is not { } recipeVersion
                || recipeVersion < ProductionRecipeVersion.Minimum)
            {
                return Validation(
                    "productionRecipe.version",
                    $"A concrete production recipe version of at least {ProductionRecipeVersion.Minimum} is required.");
            }

            productionRecipe = new ProductionRecipeReference
            {
                Id = recipeId,
                Version = new ProductionRecipeVersion(recipeVersion)
            };
        }

        // Optional explicit identity selection. The caller supplies an authoring
        // reference; this endpoint never resolves a version or touches the registry.
        IReadOnlyList<AssetReference>? identityReferences = null;
        if (request?.IdentityReference is { } selection)
        {
            if (!AssetReferenceId.TryParse(selection.AssetId, out var assetId))
            {
                return Validation(
                    "identityReference.assetId",
                    "A valid lowercase asset reference id is required.");
            }

            if (selection.Version is { } version && version < IdentityAssetVersion.Minimum)
            {
                return Validation(
                    "identityReference.version",
                    $"An identity asset version must be at least {IdentityAssetVersion.Minimum}.");
            }

            identityReferences =
            [
                new AssetReference
                {
                    AssetId = assetId,
                    Version = selection.Version is { } value
                        ? new IdentityAssetVersion(value)
                        : null
                }
            ];
        }

        try
        {
            var jobId = await workflow.EnqueueAsync(
                parsedProjectId,
                parsedStoryboardJobId,
                force ?? false,
                productionRecipe,
                identityReferences,
                cancellationToken);

            if (jobId is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Content project not found.",
                    detail: $"Content project '{parsedProjectId}' does not exist.");
            }

            return Results.Accepted(
                $"/api/jobs/{jobId}",
                new EnqueueJobResponse(jobId.Value, "queued"));
        }
        catch (SceneVisualGenerationException exception)
        {
            return VisualProblem(exception);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [exception.ParamName ?? "request"] = [exception.Message]
                });
        }
    }

    private static IResult VisualProblem(SceneVisualGenerationException exception) =>
        exception.ErrorCode switch
        {
            "visual_storyboard_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Storyboard not found.",
                detail: exception.Message),
            // Materialization failures (reference not found / not Approved) share one
            // canonical error code from the workflow, so they map to the existing
            // conflict outcome rather than guessing 404 vs 409 here.
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Visual generation cannot start.",
                detail: exception.Message)
        };

    private static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [message]
        });

    private static bool TryParseIdentifier(string value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;

    private static IResult InvalidIdentifier(string fieldName) =>
        Validation(fieldName, "A non-empty GUID is required.");
}

/// <summary>
/// Optional visual-enqueue body. v1 exposes at most one selected production recipe
/// and at most one selected identity reference; both are optional and additive, so a
/// body-less request remains valid and unchanged. The recipe names an exact version
/// (no "latest"); the identity reference is an authoring selection whose version is
/// optional. Materialization stays inside <see cref="GenerateSceneVisualsWorkflow"/>.
/// </summary>
public sealed record EnqueueVisualsRequest(
    VisualIdentityReference? IdentityReference,
    VisualProductionRecipe? ProductionRecipe = null);

/// <summary>
/// Transport selection of a production format: a stable recipe id and a concrete
/// version. It is product intent, not provider configuration, and carries no
/// provider, path, or capability detail.
/// </summary>
public sealed record VisualProductionRecipe(string? Id, int? Version);

/// <summary>
/// An authoring selection only: a concrete version is optional, and the workflow
/// resolves the Approved version exactly once. Never a resolved pinned asset, and
/// never a bare <c>(assetId, version)</c> pin.
/// </summary>
public sealed record VisualIdentityReference(string? AssetId, int? Version);
