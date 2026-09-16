using AIStudio.Application.Jobs;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Jobs;

public sealed class JobReader(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IJobReader
{
    public async Task<JobSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Jobs
            .AsNoTracking()
            .Where(job => job.Id == id)
            .Select(job => new JobSnapshot(
                job.Id,
                job.ContentProjectId,
                job.Type,
                job.Status,
                job.RetryCount,
                job.MaxRetries,
                job.Result,
                job.ErrorCode,
                job.ErrorSummary,
                job.CreatedAt,
                job.UpdatedAt,
                job.StartedAt,
                job.CompletedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
