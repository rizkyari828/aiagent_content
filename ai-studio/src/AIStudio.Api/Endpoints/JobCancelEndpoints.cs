using AIStudio.Application.Jobs;
using AIStudio.Domain.Jobs;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// Operator job-cancel HTTP surface. It sits next to the existing
/// <c>GET /api/jobs/{jobId}</c> because cancellation is a job-level operation and needs
/// no content-project context; the workflow and queue own the guarded transition.
/// </summary>
public static class JobCancelEndpoints
{
    public static IEndpointRouteBuilder MapJobCancelApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/jobs/{jobId}/cancel", CancelAsync)
            .WithName("CancelJob")
            .Produces<JobCancelResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        return endpoints;
    }

    internal static async Task<IResult> CancelAsync(
        string jobId,
        CancelJobWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(jobId, out var parsedJobId) || parsedJobId == Guid.Empty)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["jobId"] = ["A non-empty GUID is required."]
                },
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "job_cancel_invalid_request"
                });
        }

        var result = await workflow.CancelAsync(parsedJobId, cancellationToken);
        var currentStatus = result.Status ?? JobStatus.Cancelled;
        return result.Outcome switch
        {
            JobCancelOutcome.Cancelled => Results.Ok(
                new JobCancelResponse(parsedJobId, ApiStatus.ToApiStatus(currentStatus))),
            JobCancelOutcome.NotFound => CodedProblem(
                StatusCodes.Status404NotFound,
                "Job not found.",
                $"Job '{parsedJobId}' does not exist.",
                "job_not_found"),
            JobCancelOutcome.NotAllowed => CodedProblem(
                StatusCodes.Status409Conflict,
                "Job cancellation is not allowed.",
                $"Job '{parsedJobId}' is already {ApiStatus.ToApiStatus(currentStatus)}; only queued or running jobs can be cancelled.",
                "job_cancel_not_allowed",
                new Dictionary<string, object?>
                {
                    ["status"] = ApiStatus.ToApiStatus(currentStatus)
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null)
        };
    }

    private static IResult CodedProblem(
        int statusCode,
        string title,
        string detail,
        string code,
        IReadOnlyDictionary<string, object?>? extraExtensions = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code
        };

        if (extraExtensions is not null)
        {
            foreach (var extension in extraExtensions)
            {
                extensions[extension.Key] = extension.Value;
            }
        }

        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: extensions);
    }
}

public sealed record JobCancelResponse(Guid JobId, string Status);
