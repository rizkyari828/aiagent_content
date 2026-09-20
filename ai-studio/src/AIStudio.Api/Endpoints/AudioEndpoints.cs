using AIStudio.Application.Jobs.GenerateAudio;
using AIStudio.Application.Rendering.AudioProduction;

namespace AIStudio.Api.Endpoints;

public static class AudioEndpoints
{
    public static IEndpointRouteBuilder MapAudioApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost(
                "/content-projects/{contentProjectId}/storyboard-jobs/{storyboardJobId}/audio-jobs",
                EnqueueAudioAsync)
            .WithName("EnqueueGenerateAudio")
            .Produces<EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> EnqueueAudioAsync(
        string contentProjectId,
        string storyboardJobId,
        GenerateAudioWorkflow workflow,
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
        catch (AudioProductionException exception)
        {
            return exception.ErrorCode switch
            {
                "audio_storyboard_not_found" => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Storyboard not found.",
                    detail: exception.Message),
                _ => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Audio production cannot start.",
                    detail: exception.Message)
            };
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

    private static bool TryParseIdentifier(string value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;

    private static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            });
}
