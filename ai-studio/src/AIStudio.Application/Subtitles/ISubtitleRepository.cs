using AIStudio.Domain.Subtitles;

namespace AIStudio.Application.Subtitles;

public interface ISubtitleRepository
{
    Task<SubtitleTrack?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    void Add(SubtitleTrack track);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
