using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.FinalVideoQa;

public sealed class FinalVideoQaWorkflow(
    IApplicationDbContext dbContext,
    IContentProjectReader contentProjects,
    IJobReader jobs,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        Guid renderJobId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(renderJobId, nameof(renderJobId));

        var project = await contentProjects.FindByIdAsync(
            contentProjectId,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        var renderJob = await jobs.FindByIdAsync(renderJobId, cancellationToken);
        if (renderJob is null || renderJob.ContentProjectId != contentProjectId)
        {
            throw Error(
                "qa_render_job_not_found",
                $"RenderVideo job '{renderJobId}' does not exist for this content project.");
        }

        if (renderJob.Type != JobType.RenderVideo
            || renderJob.Status != JobStatus.Succeeded
            || renderJob.Result is null)
        {
            throw Error(
                "qa_render_job_invalid",
                "Final QA requires a completed RenderVideo job result.");
        }

        RenderVideoResult renderResult;
        try
        {
            renderResult = RenderVideoResult.Deserialize(renderJob.Result);
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "qa_render_result_invalid",
                "The RenderVideo result is not a valid structured result.",
                exception);
        }

        var payload = JsonSerializer.Serialize(
            new FinalVideoQaJobPayload(contentProjectId, renderJobId),
            JsonOptions);

        var job = Job.Create(
            contentProjectId,
            JobType.FinalVideoQa,
            ComputeInputVersionHash(payload, renderResult),
            payload,
            timeProvider.GetUtcNow());

        dbContext.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return job.Id;
    }

    private static string ComputeInputVersionHash(
        string payload,
        RenderVideoResult renderResult)
    {
        var builder = new StringBuilder(payload)
            .Append('\n')
            .Append(renderResult.Serialize());

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }

    private static FinalVideoQaException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
