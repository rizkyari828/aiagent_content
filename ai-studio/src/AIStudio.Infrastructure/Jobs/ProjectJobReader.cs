using AIStudio.Application.Jobs;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Jobs;

/// <summary>
/// Project-scoped job projection. Only identifying/status/timestamp columns are read;
/// the persisted payload and result stay inside the job module.
/// </summary>
public sealed class ProjectJobReader(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IProjectJobReader
{
    public async Task<ProjectJobSummary?> FindLatestAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Jobs
            .AsNoTracking()
            .Where(job => job.ContentProjectId == contentProjectId)
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .Select(job => new ProjectJobSummary(
                job.Id,
                job.Type,
                job.Status,
                job.CreatedAt,
                job.UpdatedAt,
                job.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProjectJobSummary?> FindLatestAsync(
        Guid contentProjectId,
        JobType type,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Jobs
            .AsNoTracking()
            .Where(job => job.ContentProjectId == contentProjectId && job.Type == type)
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .Select(job => new ProjectJobSummary(
                job.Id,
                job.Type,
                job.Status,
                job.CreatedAt,
                job.UpdatedAt,
                job.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
