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

        api.MapGet(
                "/content-projects/{contentProjectId}/subtitle/content",
                GetSubtitleContentAsync)
            .WithName("GetSubtitleContent")
            .Produces<SubtitleContentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapPut(
                "/content-projects/{contentProjectId}/subtitle/content",
                UpdateSubtitleContentAsync)
            .WithName("UpdateSubtitleContent")
            .Produces<SubtitleContentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
                detail: $"Content project '{parsedProjectId}' does not have a subtitle track.",
                extensions: new Dictionary<string, object?> { ["code"] = "subtitle_not_found" })
            : Results.Ok(SubtitleResponse.From(subtitle));
    }

    internal static async Task<IResult> GetSubtitleContentAsync(
        string contentProjectId,
        SubtitleWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var parsedProjectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        try
        {
            var content = await workflow.GetContentAsync(parsedProjectId, cancellationToken);
            if (content is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Content project not found.",
                    detail: $"Content project '{parsedProjectId}' does not exist.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "content_project_not_found"
                    });
            }

            var subtitle = await workflow.FindAsync(parsedProjectId, cancellationToken);
            return Results.Ok(new SubtitleContentResponse(
                parsedProjectId,
                subtitle?.Path ?? string.Empty,
                content,
                subtitle?.ByteSize ?? 0,
                subtitle?.ContentHash ?? string.Empty));
        }
        catch (SubtitleException exception)
        {
            return SubtitleProblem(exception);
        }
    }

    internal static async Task<IResult> UpdateSubtitleContentAsync(
        string contentProjectId,
        UpdateSubtitleContentRequest request,
        SubtitleWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var parsedProjectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        try
        {
            var srt = request.Content ?? request.SrtContent ?? string.Empty;
            var updated = await workflow.UpdateContentAsync(
                parsedProjectId,
                srt,
                cancellationToken);

            if (updated is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Content project not found.",
                    detail: $"Content project '{parsedProjectId}' does not exist.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "content_project_not_found"
                    });
            }

            return Results.Ok(new SubtitleContentResponse(
                updated.ContentProjectId,
                updated.Path,
                srt,
                updated.ByteSize,
                updated.ContentHash));
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
                    [exception.ParamName ?? "content"] = [exception.Message]
                },
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "subtitle_content_invalid"
                });
        }
    }

    private static IResult SubtitleProblem(SubtitleException exception) =>
        exception.ErrorCode switch
        {
            "subtitle_storyboard_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Storyboard job not found.",
                detail: exception.Message,
                extensions: new Dictionary<string, object?> { ["code"] = exception.ErrorCode }),
            "subtitle_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Subtitle not found.",
                detail: exception.Message,
                extensions: new Dictionary<string, object?> { ["code"] = exception.ErrorCode }),
            "subtitle_content_invalid" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["content"] = [exception.Message]
                },
                extensions: new Dictionary<string, object?> { ["code"] = exception.ErrorCode }),
            "subtitle_provenance_required" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["license"] = [exception.Message]
                },
                extensions: new Dictionary<string, object?> { ["code"] = exception.ErrorCode }),
            "subtitle_format_unsupported" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["path"] = [exception.Message]
                },
                extensions: new Dictionary<string, object?> { ["code"] = exception.ErrorCode }),
            "asset_path_invalid" or "asset_file_not_found"
                or "asset_file_unreadable" or "asset_file_invalid" =>
                Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["path"] = [exception.Message]
                    },
                    extensions: new Dictionary<string, object?> { ["code"] = exception.ErrorCode }),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Subtitle conflict.",
                detail: exception.Message,
                extensions: new Dictionary<string, object?> { ["code"] = exception.ErrorCode })
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

public sealed record UpdateSubtitleContentRequest(
    string? Content = null,
    string? SrtContent = null);

public sealed record SubtitleContentResponse(
    Guid ContentProjectId,
    string Path,
    string Content,
    long ByteSize,
    string ContentHash);
