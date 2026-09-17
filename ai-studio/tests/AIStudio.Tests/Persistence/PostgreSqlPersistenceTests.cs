using System.Text.Json;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class PostgreSqlPersistenceTests
{
    [Fact]
    public async Task ContentProjectAndJob_RoundTripThroughPostgreSql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set ConnectionStrings__DefaultConnection before running integration tests.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new ApplicationDbContext(options);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(cancellationToken));

        var now = DateTimeOffset.UtcNow;
        var project = ContentProject.Create(
            $"Persistence test {Guid.NewGuid():N}",
            "Disposable integration-test row.",
            now);
        var job = Job.Create(
            project.Id,
            JobType.GenerateIdea,
            $"test-{Guid.NewGuid():N}",
            """{"topic":"local AI","language":"id"}""",
            now);

        try
        {
            context.ContentProjects.Add(project);
            context.Jobs.Add(job);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();

            var persistedJob = await context.Jobs
                .SingleAsync(
                    candidate =>
                        candidate.Id == job.Id
                        && candidate.Status == JobStatus.Queued,
                    cancellationToken);

            Assert.Equal(project.Id, persistedJob.ContentProjectId);
            using (var payload = JsonDocument.Parse(persistedJob.Payload))
            {
                Assert.Equal("local AI", payload.RootElement.GetProperty("topic").GetString());
                Assert.Equal("id", payload.RootElement.GetProperty("language").GetString());
            }

            persistedJob.Start(
                "integration-test",
                now.AddMinutes(5),
                now.AddMinutes(1));
            persistedJob.Succeed(
                """{"idea":"Run AI locally"}""",
                now.AddMinutes(2));
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();

            var completedJob = await context.Jobs.SingleAsync(
                candidate => candidate.Id == job.Id,
                cancellationToken);
            Assert.Equal(JobStatus.Succeeded, completedJob.Status);
            using var result = JsonDocument.Parse(completedJob.Result!);
            Assert.Equal("Run AI locally", result.RootElement.GetProperty("idea").GetString());
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.Jobs
                .Where(candidate => candidate.Id == job.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await context.ContentProjects
                .Where(candidate => candidate.Id == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
