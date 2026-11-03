using AIStudio.Application.Approvals;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Approvals;

/// <summary>
/// Approval persistence plus read-only job evidence lookup. The job rows are never
/// mutated here; only <c>final_approvals</c> is written.
/// </summary>
public sealed class FinalApprovalRepository(ApplicationDbContext dbContext)
    : IFinalApprovalRepository
{
    public Task<FinalApproval?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        dbContext.FinalApprovals.SingleOrDefaultAsync(
            approval => approval.ContentProjectId == contentProjectId,
            cancellationToken);

    public async Task<FinalVideoQaEvidence?> FindLatestQaAsync(
        Guid contentProjectId,
        Guid renderJobId,
        CancellationToken cancellationToken)
    {
        // The render id lives inside the JSON payload, so candidates are narrowed in
        // SQL by project/type/terminal status and matched in memory. A project has few
        // QA jobs. The newest terminal job is authoritative: a later failed QA must
        // invalidate an earlier pass instead of being silently ignored.
        var candidates = await dbContext.Jobs
            .AsNoTracking()
            .Where(candidate =>
                candidate.ContentProjectId == contentProjectId
                && candidate.Type == JobType.FinalVideoQa
                && (candidate.Status == JobStatus.Succeeded
                    || candidate.Status == JobStatus.Failed)
                && candidate.CompletedAt != null)
            .OrderByDescending(candidate => candidate.CompletedAt)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var evidence = ToEvidence(candidate, renderJobId);
            if (evidence is not null)
            {
                return evidence;
            }
        }

        return null;
    }

    public Task<bool> HasNewerRenderAsync(
        Guid contentProjectId,
        Guid renderJobId,
        DateTimeOffset renderCreatedAt,
        CancellationToken cancellationToken) =>
        dbContext.Jobs
            .AsNoTracking()
            .AnyAsync(
                candidate =>
                    candidate.ContentProjectId == contentProjectId
                    && candidate.Type == JobType.RenderVideo
                    && candidate.Id != renderJobId
                    && candidate.CreatedAt > renderCreatedAt,
                cancellationToken);

    public void Add(FinalApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        dbContext.FinalApprovals.Add(approval);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private static FinalVideoQaEvidence? ToEvidence(Job job, Guid renderJobId)
    {
        FinalVideoQaJobPayload payload;
        try
        {
            payload = FinalVideoQaJobPayload.Deserialize(job.Payload);
        }
        catch (JobExecutionException)
        {
            return null;
        }

        return payload.RenderJobId == renderJobId && payload.ContentProjectId == job.ContentProjectId
            ? new FinalVideoQaEvidence(
                job.Id,
                job.Result,
                job.Status == JobStatus.Succeeded,
                job.CompletedAt!.Value)
            : null;
    }
}
