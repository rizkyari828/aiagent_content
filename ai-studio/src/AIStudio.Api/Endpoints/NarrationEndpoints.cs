using AIStudio.Application.Narration;
using AIStudio.Domain.Assets;

namespace AIStudio.Api.Endpoints;

public static class NarrationEndpoints
{
    public static IEndpointRouteBuilder MapNarrationApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost(
                "/content-projects/{contentProjectId}/storyboard-jobs/{jobId}/narration",
                RegisterNarrationAsync)
            .WithName("RegisterNarration")
            .Produces<NarrationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        api.MapGet(
                "/content-projects/{contentProjectId}/narration",
                GetNarrationAsync)
            .WithName("GetNarration")
            .Produces<NarrationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> RegisterNarrationAsync(
        string contentProjectId,
        string jobId,
        RegisterNarrationRequest request,
        NarrationWorkflow workflow,
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

        if (!TryParseEnum(request.Origin, out AssetOrigin origin))
        {
            return InvalidEnum("origin");
        }

        try
        {
            var narration = await workflow.RegisterAsync(
                parsedProjectId,
                parsedJobId,
                new RegisterNarration(
                    request.Path,
                    origin,
                    request.Source,
                    request.Creator,
                    request.License,
                    request.RetrievedAt),
                cancellationToken);

            if (narration is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Content project not found.",
                    detail: $"Content project '{parsedProjectId}' does not exist.");
            }

            return Results.Created(
                $"/api/content-projects/{parsedProjectId}/narration",
                NarrationResponse.From(narration));
        }
        catch (NarrationException exception)
        {
            return NarrationProblem(exception);
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

    private static async Task<IResult> GetNarrationAsync(
        string contentProjectId,
        NarrationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var parsedProjectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        var narration = await workflow.FindAsync(parsedProjectId, cancellationToken);
        return narration is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Narration not found.",
                detail: $"Content project '{parsedProjectId}' does not have a narration track.")
            : Results.Ok(NarrationResponse.From(narration));
    }

    private static IResult NarrationProblem(NarrationException exception) =>
        exception.ErrorCode switch
        {
            "narration_storyboard_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Storyboard job not found.",
                detail: exception.Message),
            "narration_provenance_required" => Results.ValidationProblem(
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
                title: "Narration conflict.",
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

public sealed record RegisterNarrationRequest(
    string Path,
    string Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt);

public sealed record NarrationResponse(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceJobId,
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
    public static NarrationResponse From(NarrationSnapshot narration) =>
        new(
            narration.Id,
            narration.ContentProjectId,
            narration.SourceJobId,
            narration.Path,
            narration.ByteSize,
            narration.ContentHash,
            narration.Origin.ToString().ToLowerInvariant(),
            narration.Source,
            narration.Creator,
            narration.License,
            narration.RetrievedAt,
            narration.CreatedAt);
}
