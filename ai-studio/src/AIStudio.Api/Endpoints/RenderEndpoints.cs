using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Rendering;

namespace AIStudio.Api.Endpoints;

public static class RenderEndpoints
{
    public static IEndpointRouteBuilder MapRenderApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost(
                "/content-projects/{contentProjectId}/storyboard-jobs/{storyboardJobId}/render-jobs",
                EnqueueRenderVideoAsync)
            .WithName("EnqueueRenderVideo")
            .Produces<EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> EnqueueRenderVideoAsync(
        string contentProjectId,
        string storyboardJobId,
        string? variant,
        RenderVideoWorkflow workflow,
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
                variant,
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
        catch (RenderVideoException exception)
        {
            return RenderProblem(exception);
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

    private static IResult RenderProblem(RenderVideoException exception) =>
        exception.ErrorCode switch
        {
            "render_storyboard_not_found" or "render_narration_not_found" =>
                Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Render input not found.",
                    detail: exception.Message),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Render cannot start.",
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
