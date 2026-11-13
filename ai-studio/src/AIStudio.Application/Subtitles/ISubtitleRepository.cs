using AIStudio.Domain.Subtitles;

namespace AIStudio.Application.Subtitles;

public interface ISubtitleRepository
{
    Task<SubtitleTrack?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    void Add(SubtitleTrack track);

    Task UpdateAsync(SubtitleTrack track, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
