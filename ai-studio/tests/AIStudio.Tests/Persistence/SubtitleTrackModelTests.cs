using AIStudio.Domain.Subtitles;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class SubtitleTrackModelTests
{
    [Fact]
    public void Model_MapsOneSubtitleTrackPerProjectWithProvenance()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model;Username=model;Password=model")
            .Options;
        using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(typeof(SubtitleTrack));

        Assert.NotNull(entity);
        Assert.Equal("subtitle_tracks", entity.GetTableName());
        Assert.Equal(
            SubtitleTrack.ContentHashLength,
            entity.FindProperty(nameof(SubtitleTrack.ContentHash))!.GetMaxLength());
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Single().Name == nameof(SubtitleTrack.ContentProjectId));
        Assert.Equal(2, entity.GetForeignKeys().Count());
    }
}
