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

    public void Add(ContentProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ContentProjects.Add(project);
    }

    public void Add(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Jobs.Add(job);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
