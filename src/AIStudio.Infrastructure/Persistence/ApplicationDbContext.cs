using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<ContentProject> ContentProjects => Set<ContentProject>();

    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
