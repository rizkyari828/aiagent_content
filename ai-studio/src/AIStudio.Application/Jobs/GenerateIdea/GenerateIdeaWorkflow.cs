using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.GenerateIdea;

public sealed class GenerateIdeaWorkflow(
    IApplicationDbContext dbContext,
    IContentProjectReader contentProjects,
    IJobReader jobs,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<ContentProjectSnapshot> CreateProjectAsync(
        string title,
        string? brief,
        CancellationToken cancellationToken)
    {
        var project = ContentProject.Create(title, brief, timeProvider.GetUtcNow());
        dbContext.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ContentProjectSnapshot(project.Id, project.Title, project.Brief);
    }

    public async Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        string topic,
        string? targetAudience,
        string? language,
        CancellationToken cancellationToken)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException(
                "Content project ID cannot be empty.",
                nameof(contentProjectId));
        }

        var rawPayload = JsonSerializer.Serialize(
            new GenerateIdeaJobPayload(
                contentProjectId,
                topic,
                targetAudience,
                language),
            JsonOptions);
        var normalizedPayload = GenerateIdeaJobPayload.Deserialize(rawPayload);
        var payload = JsonSerializer.Serialize(normalizedPayload, JsonOptions);
        var inputVersionHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

        var project = await contentProjects.FindByIdAsync(
            contentProjectId,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        var job = Job.Create(
            contentProjectId,
            JobType.GenerateIdea,
            inputVersionHash,
            payload,
            timeProvider.GetUtcNow());

        dbContext.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return job.Id;
    }

    public async Task<JobDetails?> FindJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Job ID cannot be empty.", nameof(jobId));
        }

        var job = await jobs.FindByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        object? result = job.Status == JobStatus.Succeeded && job.Result is not null
            ? job.Type switch
            {
                JobType.GenerateIdea => GenerateIdeaResult.Deserialize(job.Result),
                JobType.GenerateScript => GenerateScriptResult.Deserialize(job.Result),
                JobType.GenerateStoryboard => GenerateStoryboardResult.Deserialize(job.Result),
                _ => null
            }
            : null;

        return new JobDetails(
            job.Id,
            job.ContentProjectId,
            job.Type,
            job.Status,
            job.RetryCount,
            job.MaxRetries,
            result,
            job.ErrorCode,
            job.ErrorSummary,
            job.CreatedAt,
            job.UpdatedAt,
            job.StartedAt,
            job.CompletedAt);
    }
}

public sealed record JobDetails(
    Guid Id,
    Guid ContentProjectId,
    JobType Type,
    JobStatus Status,
    int RetryCount,
    int MaxRetries,
    object? Result,
    string? ErrorCode,
    string? ErrorSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
