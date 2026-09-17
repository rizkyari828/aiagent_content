using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Scripts;
using AIStudio.Domain.Scripts;

namespace AIStudio.Api.Endpoints;

public static class ScriptReviewEndpoints
{
    public static IEndpointRouteBuilder MapScriptReviewApi(
        this IEndpointRouteBuilder endpoints)
    {
        var review = endpoints.MapGroup(
            "/api/content-projects/{contentProjectId}/script-review");

        review.MapPost("", StartReviewAsync)
            .WithName("StartScriptReview")
            .Produces<ReviewedScriptResponse>(StatusCodes.Status201Created)
            .Produces<ReviewedScriptResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        review.MapGet("", GetReviewAsync)
            .WithName("GetScriptReview")
            .Produces<ReviewedScriptResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        review.MapPut("", EditReviewAsync)
            .WithName("EditScriptReview")
            .Produces<ReviewedScriptResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        review.MapPost("/approve", ApproveReviewAsync)
            .WithName("ApproveScriptReview")
            .Produces<ReviewedScriptResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> StartReviewAsync(
        string contentProjectId,
        StartScriptReviewRequest request,
        ScriptReviewWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        if (request.SourceJobId == Guid.Empty)
        {
            return InvalidIdentifier("sourceJobId");
        }

        try
        {
            var result = await workflow.StartAsync(
                projectId,
                request.SourceJobId,
                cancellationToken);
            var response = ReviewedScriptResponse.From(result.Script);

            return result.Created
                ? Results.Created(
                    $"/api/content-projects/{projectId}/script-review",
                    response)
                : Results.Ok(response);
        }
        catch (ScriptReviewException exception)
        {
            return ScriptReviewProblem(exception);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> GetReviewAsync(
        string contentProjectId,
        ScriptReviewWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        var script = await workflow.FindAsync(projectId, cancellationToken);
        return script is null
            ? ScriptNotFound(projectId)
            : Results.Ok(ReviewedScriptResponse.From(script));
    }

    private static async Task<IResult> EditReviewAsync(
        string contentProjectId,
        EditScriptReviewRequest request,
        ScriptReviewWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        try
        {
            var script = await workflow.EditAsync(
                projectId,
                request.Script,
                cancellationToken);
            return script is null
                ? ScriptNotFound(projectId)
                : Results.Ok(ReviewedScriptResponse.From(script));
        }
        catch (ScriptReviewException exception)
        {
            return ScriptReviewProblem(exception);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> ApproveReviewAsync(
        string contentProjectId,
        ScriptReviewWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        var script = await workflow.ApproveAsync(projectId, cancellationToken);
        return script is null
            ? ScriptNotFound(projectId)
            : Results.Ok(ReviewedScriptResponse.From(script));
    }

    private static bool TryParseIdentifier(string value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;

    private static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            });

    private static IResult InvalidRequest(ArgumentException exception) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "request"] = [exception.Message]
            });

    private static IResult ScriptReviewProblem(ScriptReviewException exception) =>
        exception.ErrorCode switch
        {
            "script_review_source_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "GenerateScript job not found.",
                detail: exception.Message),
            "script_review_invalid_content" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["script"] = [exception.Message]
                }),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Script review conflict.",
                detail: exception.Message)
        };

    private static IResult ScriptNotFound(Guid contentProjectId) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Reviewed script not found.",
            detail: $"Content project '{contentProjectId}' does not have a reviewed script.");
}

public sealed record StartScriptReviewRequest(Guid SourceJobId);

public sealed record EditScriptReviewRequest(GenerateScriptResult Script);

public sealed record ReviewedScriptResponse(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceJobId,
    GenerateScriptResult Script,
    string Status,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ApprovedAt)
{
    public static ReviewedScriptResponse From(ReviewedScriptSnapshot script) =>
        new(
            script.Id,
            script.ContentProjectId,
            script.SourceJobId,
            script.Script,
            ToApiStatus(script.Status),
            script.Revision,
            script.CreatedAt,
            script.UpdatedAt,
            script.ApprovedAt);

    private static string ToApiStatus(ScriptReviewStatus status) =>
        status switch
        {
            ScriptReviewStatus.Draft => "draft",
            ScriptReviewStatus.Approved => "approved",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
