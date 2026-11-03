using AIStudio.Domain.Approvals;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class FinalApprovalModelTests
{
    [Fact]
    public void Model_MapsOneApprovalPerProjectBoundToRenderAndQaJobs()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(FinalApproval));

        Assert.NotNull(entity);
        Assert.Equal("final_approvals", entity.GetTableName());
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Single().Name == nameof(FinalApproval.ContentProjectId));
        Assert.Contains(
            entity.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Job)
                && foreignKey.Properties.Single().Name == nameof(FinalApproval.SourceRenderJobId));
        Assert.Equal(2, entity.GetForeignKeys().Count());
    }

    [Fact]
    public void Model_MapsOnePublicationPerProjectBoundToTheApproval()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(PublicationRecord));

        Assert.NotNull(entity);
        Assert.Equal("publication_records", entity.GetTableName());
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Single().Name == nameof(PublicationRecord.ContentProjectId));
        Assert.Contains(
            entity.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(FinalApproval));
        Assert.Equal(2, entity.GetForeignKeys().Count());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model;Username=model;Password=model")
            .Options;
        return new ApplicationDbContext(options);
    }
}
