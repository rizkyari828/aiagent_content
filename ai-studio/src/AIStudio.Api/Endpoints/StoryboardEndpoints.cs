using AIStudio.Application.Jobs.GenerateStoryboard;

namespace AIStudio.Api.Endpoints;

public static class StoryboardEndpoints
{
    public static IEndpointRouteBuilder MapStoryboardApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost(
                "/content-projects/{contentProjectId}/storyboard-jobs",
                EnqueueGenerateStoryboardAsync)
            .WithName("EnqueueGenerateStoryboard")
            .Produces<EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> EnqueueGenerateStoryboardAsync(
        string contentProjectId,
        GenerateStoryboardWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(contentProjectId, out var parsedContentProjectId)
            || parsedContentProjectId == Guid.Empty)
        {
            return InvalidIdentifier("contentProjectId");
        }

        try
        {
            var jobId = await workflow.EnqueueAsync(
                parsedContentProjectId,
                cancellationToken);

            if (jobId is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Content project not found.",
                    detail: $"Content project '{parsedContentProjectId}' does not exist.");
            }

            return Results.Accepted(
                $"/api/jobs/{jobId}",
                new EnqueueJobResponse(jobId.Value, "queued"));
        }
        catch (StoryboardGenerationException exception)
        {
            return StoryboardProblem(exception);
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

    private static IResult StoryboardProblem(StoryboardGenerationException exception) =>
        exception.ErrorCode switch
        {
            "storyboard_script_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Reviewed script not found.",
                detail: exception.Message),
            "storyboard_script_invalid" => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["script"] = [exception.Message]
                }),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Storyboard generation conflict.",
                detail: exception.Message)
        };

    private static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            });
}
