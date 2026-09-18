using AIStudio.Domain.Narration;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class NarrationTrackModelTests
{
    [Fact]
    public void Model_MapsOneNarrationTrackPerProjectWithProvenance()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model;Username=model;Password=model")
            .Options;
        using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(typeof(NarrationTrack));

        Assert.NotNull(entity);
        Assert.Equal("narration_tracks", entity.GetTableName());
        Assert.Equal(
            NarrationTrack.ContentHashLength,
            entity.FindProperty(nameof(NarrationTrack.ContentHash))!.GetMaxLength());
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Single().Name == nameof(NarrationTrack.ContentProjectId));
        Assert.Equal(2, entity.GetForeignKeys().Count());
    }
}
