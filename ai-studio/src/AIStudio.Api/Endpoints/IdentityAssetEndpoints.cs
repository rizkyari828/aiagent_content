using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using Microsoft.AspNetCore.Mvc;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// Thin HTTP transport for the identity-asset authoring Application workflows.
/// It parses HTTP, bounds and reads the upload, then delegates: version allocation,
/// hashing, PNG policy, store writes, registry writes, and approval semantics all
/// remain in <see cref="ImportIdentityAssetWorkflow"/> and
/// <see cref="ApproveIdentityAssetWorkflow"/>.
/// </summary>
public static class IdentityAssetEndpoints
{
    /// <summary>
    /// Explicit upload policy for one reference PNG. Kestrel's default body limit
    /// remains the hard network ceiling; this is the authoring policy bound.
    /// </summary>
    internal const long MaxUploadBytes = 8L * 1024 * 1024;

    internal const string ImportRoute = "/identity-assets";
    internal const string ApproveRoute = "/identity-assets/{assetId}/versions/{version}/approve";

    public static IEndpointRouteBuilder MapIdentityAssetApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost(ImportRoute, ImportAsync)
            .WithName("ImportIdentityAsset")
            .DisableAntiforgery()
            .Produces<IdentityAssetResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        api.MapPost(ApproveRoute, Approve)
            .WithName("ApproveIdentityAsset")
            .Produces<IdentityAssetResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    internal static async Task<IResult> ImportAsync(
        [FromForm] string? assetId,
        [FromForm] IFormFile? file,
        ImportIdentityAssetWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!AssetReferenceId.TryParse(assetId, out var referenceId))
        {
            return Validation("assetId", "A valid lowercase asset reference id is required.");
        }

        if (file is null || file.Length == 0)
        {
            return Validation("file", "A non-empty PNG file is required.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Reference image is too large.",
                detail: $"A reference image must be at most {MaxUploadBytes} bytes.");
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        try
        {
            // v1 authors exactly one kind: the server fixes kind/media type rather
            // than trusting caller-supplied values.
            var asset = await workflow.ImportAsync(
                new ImportIdentityAssetRequest(
                    referenceId,
                    ImportIdentityAssetWorkflow.SupportedKind,
                    ImportIdentityAssetWorkflow.SupportedMediaType,
                    content),
                cancellationToken);

            return Results.Ok(ToResponse(asset));
        }
        catch (IdentityAssetAuthoringException exception)
        {
            return AuthoringProblem(exception);
        }
    }

    internal static IResult Approve(
        string assetId,
        int version,
        ApproveIdentityAssetWorkflow workflow)
    {
        if (!AssetReferenceId.TryParse(assetId, out var referenceId))
        {
            return Validation("assetId", "A valid lowercase asset reference id is required.");
        }

        if (version < IdentityAssetVersion.Minimum)
        {
            return Validation("version", "A positive concrete identity asset version is required.");
        }

        try
        {
            return Results.Ok(ToResponse(
                workflow.Approve(referenceId, new IdentityAssetVersion(version))));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Identity asset version not found.",
                detail: exception.Message);
        }
        catch (IdentityAssetAuthoringException exception)
        {
            return AuthoringProblem(exception);
        }
    }

    private static IdentityAssetResponse ToResponse(IdentityAsset asset) =>
        new(
            asset.Id.Value,
            asset.Version.Value,
            asset.Kind,
            asset.MediaType,
            asset.ByteSize,
            asset.ContentHash,
            asset.Status.ToString(),
            asset.ApprovedAt,
            asset.CreatedAt,
            asset.Provenance);

    private static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [message]
        });

    private static IResult AuthoringProblem(IdentityAssetAuthoringException exception)
    {
        var statusCode = exception.ErrorCode switch
        {
            "identity_import_media_type_unsupported" => StatusCodes.Status415UnsupportedMediaType,
            "identity_import_kind_unsupported" => StatusCodes.Status422UnprocessableEntity,
            "identity_import_store_mismatch" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Problem(
            statusCode: statusCode,
            title: "Identity asset request rejected.",
            detail: exception.Message,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = exception.ErrorCode
            });
    }
}

/// <summary>Application-safe identity-asset projection: no path, storage key, or provider detail.</summary>
public sealed record IdentityAssetResponse(
    string AssetId,
    int Version,
    string Kind,
    string MediaType,
    long ByteSize,
    string ContentHash,
    string Status,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    IdentityAssetProvenance Provenance);
