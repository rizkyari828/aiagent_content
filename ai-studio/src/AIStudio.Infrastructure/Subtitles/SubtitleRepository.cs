using AIStudio.Application.Subtitles;
using AIStudio.Domain.Subtitles;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Subtitles;

public sealed class SubtitleRepository(ApplicationDbContext dbContext) : ISubtitleRepository
{
    public Task<SubtitleTrack?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        dbContext.SubtitleTracks.SingleOrDefaultAsync(
            track => track.ContentProjectId == contentProjectId,
            cancellationToken);

    public void Add(SubtitleTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        dbContext.SubtitleTracks.Add(track);
    }

    public Task UpdateAsync(SubtitleTrack track, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);
        dbContext.SubtitleTracks.Update(track);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
