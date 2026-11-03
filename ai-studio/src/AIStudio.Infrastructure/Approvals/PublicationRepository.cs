using AIStudio.Application.Approvals;
using AIStudio.Domain.Approvals;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Approvals;

public sealed class PublicationRepository(ApplicationDbContext dbContext)
    : IPublicationRepository
{
    public Task<PublicationRecord?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        dbContext.PublicationRecords.SingleOrDefaultAsync(
            record => record.ContentProjectId == contentProjectId,
            cancellationToken);

    public void Add(PublicationRecord publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        dbContext.PublicationRecords.Add(publication);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
