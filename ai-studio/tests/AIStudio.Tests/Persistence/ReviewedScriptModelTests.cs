using AIStudio.Domain.Scripts;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class ReviewedScriptModelTests
{
    [Fact]
    public void Model_MapsOneCurrentJsonScriptWithSourceProvenance()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model;Username=model;Password=model")
            .Options;
        using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(typeof(ReviewedScript));

        Assert.NotNull(entity);
        Assert.Equal("reviewed_scripts", entity.GetTableName());
        Assert.Equal(
            "jsonb",
            entity.FindProperty(nameof(ReviewedScript.Content))!.GetColumnType());
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Single().Name == nameof(ReviewedScript.ContentProjectId));
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Single().Name == nameof(ReviewedScript.SourceJobId));
        Assert.Equal(2, entity.GetForeignKeys().Count());
    }
}
