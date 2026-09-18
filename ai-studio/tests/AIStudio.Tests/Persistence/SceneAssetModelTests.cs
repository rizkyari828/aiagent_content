using AIStudio.Domain.Assets;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class SceneAssetModelTests
{
    [Fact]
    public void Model_MapsOneAssetPerProjectSceneWithProvenance()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model;Username=model;Password=model")
            .Options;
        using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(typeof(SceneAsset));

        Assert.NotNull(entity);
        Assert.Equal("scene_assets", entity.GetTableName());
        Assert.Equal(
            SceneAsset.ContentHashLength,
            entity.FindProperty(nameof(SceneAsset.ContentHash))!.GetMaxLength());
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    new[] { nameof(SceneAsset.ContentProjectId), nameof(SceneAsset.SceneIndex) }));
        Assert.Equal(2, entity.GetForeignKeys().Count());
    }
}
