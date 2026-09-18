using AIStudio.Application.Assets;
using AIStudio.Domain.Assets;

namespace AIStudio.Api.Endpoints;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost(
                "/content-projects/{contentProjectId}/storyboard-jobs/{jobId}/assets",
                RegisterSceneAssetAsync)
            .WithName("RegisterSceneAsset")
            .Produces<SceneAssetResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        api.MapGet(
                "/content-projects/{contentProjectId}/assets",
                ListSceneAssetsAsync)
            .WithName("ListSceneAssets")
            .Produces<IReadOnlyList<SceneAssetResponse>>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> RegisterSceneAssetAsync(
        string contentProjectId,
        string jobId,
        RegisterSceneAssetRequest request,
        AssetCollectionWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var parsedProjectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        if (!TryParseIdentifier(jobId, out var parsedJobId))
        {
            return InvalidIdentifier("jobId");
        }

        if (!TryParseEnum(request.Type, out AssetType type))
        {
            return InvalidEnum("type");
        }

        if (!TryParseEnum(request.Origin, out AssetOrigin origin))
        {
            return InvalidEnum("origin");
        }

        try
        {
            var asset = await workflow.RegisterAsync(
                parsedProjectId,
                parsedJobId,
                new RegisterSceneAsset(
                    request.SceneIndex,
                    request.Path,
                    type,
                    origin,
                    request.Source,
                    request.Creator,
                    request.License,
                    request.RetrievedAt),
                cancellationToken);

            if (asset is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Content project not found.",
                    detail: $"Content project '{parsedProjectId}' does not exist.");
            }

            return Results.Created(
                $"/api/content-projects/{parsedProjectId}/assets",
                SceneAssetResponse.From(asset));
        }
        catch (AssetCollectionException exception)
        {
            return AssetProblem(exception);
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

    private static async Task<IResult> ListSceneAssetsAsync(
        string contentProjectId,
        AssetCollectionWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var parsedProjectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        var assets = await workflow.ListAsync(parsedProjectId, cancellationToken);
        return Results.Ok(assets.Select(SceneAssetResponse.From).ToArray());
    }

    private static IResult AssetProblem(AssetCollectionException exception) =>
        exception.ErrorCode switch
        {
            "asset_storyboard_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Storyboard job not found.",
                detail: exception.Message),
            "asset_scene_not_found" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["sceneIndex"] = [exception.Message]
                }),
            "asset_provenance_required" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["license"] = [exception.Message]
                }),
            "asset_path_invalid" or "asset_file_not_found"
                or "asset_file_unreadable" or "asset_file_invalid" =>
                Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["path"] = [exception.Message]
                    }),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Asset collection conflict.",
                detail: exception.Message)
        };

    private static bool TryParseIdentifier(string value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;

    private static bool TryParseEnum<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed)
        && Enum.IsDefined(parsed);

    private static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            });

    private static IResult InvalidEnum(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A supported value is required."]
            });
}

public sealed record RegisterSceneAssetRequest(
    int SceneIndex,
    string Path,
    string Type,
    string Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt);

public sealed record SceneAssetResponse(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceJobId,
    int SceneIndex,
    string Type,
    string Path,
    long ByteSize,
    string ContentHash,
    string Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt,
    DateTimeOffset CreatedAt)
{
    public static SceneAssetResponse From(SceneAssetSnapshot asset) =>
        new(
            asset.Id,
            asset.ContentProjectId,
            asset.SourceJobId,
            asset.SceneIndex,
            asset.Type.ToString().ToLowerInvariant(),
            asset.Path,
            asset.ByteSize,
            asset.ContentHash,
            asset.Origin.ToString().ToLowerInvariant(),
            asset.Source,
            asset.Creator,
            asset.License,
            asset.RetrievedAt,
            asset.CreatedAt);
}
