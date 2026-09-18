using AIStudio.Application.Narration;
using AIStudio.Domain.Narration;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Narration;

public sealed class NarrationRepository(ApplicationDbContext dbContext) : INarrationRepository
{
    public Task<NarrationTrack?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        dbContext.NarrationTracks.SingleOrDefaultAsync(
            track => track.ContentProjectId == contentProjectId,
            cancellationToken);

    public void Add(NarrationTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        dbContext.NarrationTracks.Add(track);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
