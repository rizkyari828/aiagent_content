using AIStudio.Application.Subtitles;
using AIStudio.Domain.Subtitles;

namespace AIStudio.Tests.Subtitles;

internal sealed class StubSubtitleRepository(SubtitleTrack? subtitle = null)
    : ISubtitleRepository
{
    public SubtitleTrack? Subtitle { get; set; } = subtitle;

    public int SaveCount { get; private set; }

    public Task<SubtitleTrack?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            Subtitle?.ContentProjectId == contentProjectId ? Subtitle : null);
    }

    public void Add(SubtitleTrack track) => Subtitle = track;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(1);
    }
}
