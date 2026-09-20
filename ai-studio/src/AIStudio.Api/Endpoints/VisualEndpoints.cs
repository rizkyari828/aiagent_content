using AIStudio.Application.Jobs.GenerateSceneVisuals;

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

    private static async Task<IResult> EnqueueVisualsAsync(
        string contentProjectId,
        string storyboardJobId,
        bool? force,
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

        try
        {
            var jobId = await workflow.EnqueueAsync(
                parsedProjectId,
                parsedStoryboardJobId,
                force ?? false,
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
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Visual generation cannot start.",
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
