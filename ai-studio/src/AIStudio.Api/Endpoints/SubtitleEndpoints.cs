using AIStudio.Application.Subtitles;
using AIStudio.Domain.Assets;

namespace AIStudio.Api.Endpoints;

public static class SubtitleEndpoints
{
    public static IEndpointRouteBuilder MapSubtitleApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost(
                "/content-projects/{contentProjectId}/storyboard-jobs/{jobId}/subtitle",
                RegisterSubtitleAsync)
            .WithName("RegisterSubtitle")
            .Produces<SubtitleResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        api.MapGet(
                "/content-projects/{contentProjectId}/subtitle",
                GetSubtitleAsync)
            .WithName("GetSubtitle")
            .Produces<SubtitleResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> RegisterSubtitleAsync(
        string contentProjectId,
        string jobId,
        RegisterSubtitleRequest request,
        SubtitleWorkflow workflow,
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
            var subtitle = await workflow.RegisterAsync(
                parsedProjectId,
                parsedJobId,
                new RegisterSubtitle(
                    request.Path,
                    origin,
                    request.Source,
                    request.Creator,
                    request.License,
                    request.RetrievedAt),
                cancellationToken);

            if (subtitle is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Content project not found.",
                    detail: $"Content project '{parsedProjectId}' does not exist.");
            }

            return Results.Created(
                $"/api/content-projects/{parsedProjectId}/subtitle",
                SubtitleResponse.From(subtitle));
        }
        catch (SubtitleException exception)
        {
            return SubtitleProblem(exception);
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

    private static async Task<IResult> GetSubtitleAsync(
        string contentProjectId,
        SubtitleWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var parsedProjectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        var subtitle = await workflow.FindAsync(parsedProjectId, cancellationToken);
        return subtitle is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Subtitle not found.",
                detail: $"Content project '{parsedProjectId}' does not have a subtitle track.")
            : Results.Ok(SubtitleResponse.From(subtitle));
    }

    private static IResult SubtitleProblem(SubtitleException exception) =>
        exception.ErrorCode switch
        {
            "subtitle_storyboard_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Storyboard job not found.",
                detail: exception.Message),
            "subtitle_provenance_required" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["license"] = [exception.Message]
                }),
            "subtitle_format_unsupported" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["path"] = [exception.Message]
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
                title: "Subtitle conflict.",
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

public sealed record RegisterSubtitleRequest(
    string Path,
    string Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt);

public sealed record SubtitleResponse(
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
    public static SubtitleResponse From(SubtitleSnapshot subtitle) =>
        new(
            subtitle.Id,
            subtitle.ContentProjectId,
            subtitle.SourceJobId,
            subtitle.Path,
            subtitle.ByteSize,
            subtitle.ContentHash,
            subtitle.Origin.ToString().ToLowerInvariant(),
            subtitle.Source,
            subtitle.Creator,
            subtitle.License,
            subtitle.RetrievedAt,
            subtitle.CreatedAt);
}
