using AIStudio.Application.Tracking;
using AIStudio.Domain.Content;
using AIStudio.Domain.Tracking;
using AIStudio.Infrastructure.Approvals;
using AIStudio.Infrastructure.Content;
using AIStudio.Infrastructure.Persistence;
using AIStudio.Infrastructure.Tracking;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AIStudio.Tests.Persistence;

/// <summary>
/// DB-gated vertical slice for metric snapshots, time entries, and cost entries. Tracking
/// rows are append-only in v1 (no update route), so this covers round-trip, the one
/// snapshot per capture instant index, per-currency/per-kind aggregation, and the
/// restricting foreign keys. It runs only with a real PostgreSQL connection string.
/// </summary>
public sealed class TrackingVerticalSliceTests
{
    [Fact]
    public async Task Tracking_RoundTripsAndEnforcesIndexesAndForeignKeys()
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
        var factory = new TestDbContextFactory(options);
        var now = DateTimeOffset.UtcNow;
        var project = ContentProject.Create(
            $"Tracking integration {Guid.NewGuid():N}",
            "Disposable tracking vertical-slice row.",
            now);

        try
        {
            await using (var setup = new ApplicationDbContext(options))
            {
                setup.ContentProjects.Add(project);
                await setup.SaveChangesAsync(cancellationToken);
            }

            Guid firstSnapshotId;
            Guid secondSnapshotId;
            await using (var context = new ApplicationDbContext(options))
            {
                var workflows = CreateWorkflows(context, factory);

                var first = await workflows.Metrics.CreateAsync(
                    project.Id,
                    views: 0,
                    impressions: null,
                    clickThroughRate: 0.05m,
                    averageViewDurationSeconds: null,
                    watchTimeMinutes: null,
                    subscribersGained: null,
                    now.AddHours(-2),
                    "manual",
                    null,
                    cancellationToken);
                Assert.NotNull(first);
                Assert.Null(first.PublicationRecordId);
                Assert.Equal(0, first.Views);
                Assert.Null(first.Impressions);
                firstSnapshotId = first.Id;

                var second = await workflows.Metrics.CreateAsync(
                    project.Id,
                    views: 250,
                    impressions: 1000,
                    clickThroughRate: 0.25m,
                    averageViewDurationSeconds: 12.5m,
                    watchTimeMinutes: 50m,
                    subscribersGained: 3,
                    now.AddHours(-1),
                    "manual",
                    "second capture",
                    cancellationToken);
                Assert.NotNull(second);
                secondSnapshotId = second.Id;

                var duplicate = await Assert.ThrowsAsync<TrackingException>(() =>
                    workflows.Metrics.CreateAsync(
                        project.Id,
                        999,
                        null,
                        null,
                        null,
                        null,
                        null,
                        now.AddHours(-1),
                        "manual",
                        null,
                        cancellationToken));
                Assert.Equal("metric_snapshot_already_recorded", duplicate.ErrorCode);

                await workflows.TimeEntries.CreateAsync(
                    project.Id,
                    "human",
                    "content",
                    90m,
                    now.AddHours(-3),
                    null,
                    cancellationToken);
                await workflows.TimeEntries.CreateAsync(
                    project.Id,
                    "human",
                    "review",
                    30m,
                    now.AddHours(-2),
                    "review pass",
                    cancellationToken);
                await workflows.TimeEntries.CreateAsync(
                    project.Id,
                    "machine",
                    "development",
                    12.5m,
                    now.AddHours(-1),
                    null,
                    cancellationToken);

                await workflows.Costs.CreateAsync(
                    project.Id,
                    150_000m,
                    "IDR",
                    "actual",
                    "production",
                    now.AddHours(-3),
                    null,
                    cancellationToken);
                await workflows.Costs.CreateAsync(
                    project.Id,
                    50_000m,
                    "IDR",
                    "refunded",
                    "production",
                    now.AddHours(-2),
                    "partial refund",
                    cancellationToken);
                await workflows.Costs.CreateAsync(
                    project.Id,
                    4m,
                    "USD",
                    "estimated",
                    "hosting",
                    now.AddHours(-1),
                    null,
                    cancellationToken);
            }

            await using (var verification = new ApplicationDbContext(options))
            {
                var first = await verification.MetricSnapshots
                    .AsNoTracking()
                    .SingleAsync(snapshot => snapshot.Id == firstSnapshotId, cancellationToken);
                Assert.Equal(0, first.Views);
                Assert.Null(first.Impressions);
                Assert.Null(first.WatchTimeMinutes);
                Assert.Null(first.AverageViewDurationSeconds);
                Assert.Equal(0.05m, first.ClickThroughRate);
                Assert.Equal(TimeSpan.Zero, first.CapturedAt.Offset);
                Assert.Equal("manual", first.Source);

                var kinds = await verification.TimeEntries
                    .AsNoTracking()
                    .Where(entry => entry.ContentProjectId == project.Id)
                    .Select(entry => entry.Kind)
                    .ToListAsync(cancellationToken);
                Assert.Equal(3, kinds.Count);
                Assert.Equal(2, kinds.Count(kind => kind == TimeEntryKind.Human));
                Assert.Equal(1, kinds.Count(kind => kind == TimeEntryKind.Machine));

                var currencies = await verification.CostEntries
                    .AsNoTracking()
                    .Where(entry => entry.ContentProjectId == project.Id)
                    .Select(entry => entry.Currency)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                Assert.Contains("IDR", currencies);
                Assert.Contains("USD", currencies);
            }

            await using (var context = new ApplicationDbContext(options))
            {
                var workflows = CreateWorkflows(context, factory);
                var summary = await workflows.Summary.GetAsync(
                    project.Id,
                    cancellationToken);

                Assert.NotNull(summary);
                Assert.NotNull(summary.LatestMetricSnapshot);
                Assert.Equal(secondSnapshotId, summary.LatestMetricSnapshot.Id);
                Assert.Equal(250, summary.LatestMetricSnapshot.Views);
                Assert.Equal(120m, summary.HumanMinutesTotal);
                Assert.Equal(12.5m, summary.MachineMinutesTotal);

                var idr = summary.Costs.Single(cost => cost.Currency == "IDR");
                Assert.Equal(150_000m, idr.ActualTotal);
                Assert.Equal(0m, idr.EstimatedTotal);
                Assert.Equal(50_000m, idr.RefundedTotal);
                Assert.Equal(100_000m, idr.NetActualTotal);
                Assert.False(idr.RefundExceedsActual);

                var usd = summary.Costs.Single(cost => cost.Currency == "USD");
                Assert.Equal(0m, usd.ActualTotal);
                Assert.Equal(4m, usd.EstimatedTotal);
                Assert.Equal(0m, usd.NetActualTotal);
            }

            await using (var duplicate = new ApplicationDbContext(options))
            {
                duplicate.MetricSnapshots.Add(MetricSnapshot.Create(
                    project.Id,
                    null,
                    5,
                    null,
                    null,
                    null,
                    null,
                    null,
                    now.AddHours(-1),
                    "manual",
                    null,
                    now));

                var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    duplicate.SaveChangesAsync(cancellationToken));
                var postgres = Assert.IsType<PostgresException>(exception.InnerException);
                Assert.Equal(
                    TrackingRepository.MetricSnapshotUniqueIndex,
                    postgres.ConstraintName);
            }

            await using (var restricted = new ApplicationDbContext(options))
            {
                var tracked = await restricted.ContentProjects.SingleAsync(
                    candidate => candidate.Id == project.Id,
                    cancellationToken);
                restricted.ContentProjects.Remove(tracked);

                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    restricted.SaveChangesAsync(cancellationToken));
            }
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.MetricSnapshots
                .Where(snapshot => snapshot.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.TimeEntries
                .Where(entry => entry.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.CostEntries
                .Where(entry => entry.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(candidate => candidate.Id == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private static Workflows CreateWorkflows(
        ApplicationDbContext context,
        IDbContextFactory<ApplicationDbContext> factory)
    {
        var reader = new ContentProjectReader(factory);
        var lookup = new PublicationReferenceLookup(new PublicationRepository(context));
        var tracking = new TrackingRepository(context);
        var time = TimeProvider.System;

        return new Workflows(
            new MetricSnapshotWorkflow(tracking, reader, lookup, time),
            new TimeEntryWorkflow(tracking, reader, time),
            new CostEntryWorkflow(tracking, reader, time),
            new TrackingSummaryWorkflow(tracking, reader));
    }

    private sealed record Workflows(
        MetricSnapshotWorkflow Metrics,
        TimeEntryWorkflow TimeEntries,
        CostEntryWorkflow Costs,
        TrackingSummaryWorkflow Summary);

    private sealed class TestDbContextFactory(
        DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
