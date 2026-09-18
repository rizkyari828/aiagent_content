using AIStudio.Application.Jobs.FinalVideoQa;

namespace AIStudio.Api.Endpoints;

public static class FinalVideoQaEndpoints
{
    public static IEndpointRouteBuilder MapFinalVideoQaApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost(
                "/content-projects/{contentProjectId}/render-jobs/{renderJobId}/qa-jobs",
                EnqueueFinalVideoQaAsync)
            .WithName("EnqueueFinalVideoQa")
            .Produces<EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> EnqueueFinalVideoQaAsync(
        string contentProjectId,
        string renderJobId,
        FinalVideoQaWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var parsedProjectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        if (!TryParseIdentifier(renderJobId, out var parsedRenderJobId))
        {
            return InvalidIdentifier("renderJobId");
        }

        try
        {
            var jobId = await workflow.EnqueueAsync(
                parsedProjectId,
                parsedRenderJobId,
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
        catch (FinalVideoQaException exception)
        {
            return QaProblem(exception);
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

    private static IResult QaProblem(FinalVideoQaException exception) =>
        exception.ErrorCode switch
        {
            "qa_render_job_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Render job not found.",
                detail: exception.Message),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Final QA cannot start.",
                detail: exception.Message)
        };

    private static bool TryParseIdentifier(string value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;

    private static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            });
}
