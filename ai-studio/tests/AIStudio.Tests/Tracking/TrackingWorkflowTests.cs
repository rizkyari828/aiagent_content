using AIStudio.Application.Tracking;
using AIStudio.Domain.Tracking;
using Xunit;

namespace AIStudio.Tests.Tracking;

public sealed class TrackingWorkflowTests
{
    [Fact]
    public async Task CreateMetric_RecordsTheObservationAndBindsThePublication()
    {
        var publicationRecordId = Guid.NewGuid();
        var fixture = new Fixture(publicationRecordId: publicationRecordId);
        var cancellationToken = TestContext.Current.CancellationToken;

        var snapshot = await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 0,
            impressions: 1200,
            clickThroughRate: 0.05m,
            averageViewDurationSeconds: 38.5m,
            watchTimeMinutes: null,
            subscribersGained: 4,
            TrackingTestData.CapturedAt,
            " Manual ",
            "  first capture  ",
            cancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(0, snapshot.Views);
        Assert.Null(snapshot.WatchTimeMinutes);
        Assert.Equal("manual", snapshot.Source);
        Assert.Equal("first capture", snapshot.Notes);
        Assert.Equal(publicationRecordId, snapshot.PublicationRecordId);
        Assert.Equal(1, fixture.Tracking.SaveCount);
    }

    [Fact]
    public async Task CreateMetric_ReturnsNullWhenTheProjectDoesNotExist()
    {
        var fixture = new Fixture(projectExists: false);

        var snapshot = await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            10,
            null,
            null,
            null,
            null,
            null,
            TrackingTestData.CapturedAt,
            "manual",
            null,
            TestContext.Current.CancellationToken);

        Assert.Null(snapshot);
        Assert.Equal(0, fixture.Tracking.SaveCount);
    }

    [Fact]
    public async Task CreateMetric_RejectsASecondCaptureAtTheSameInstant()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            100,
            null,
            null,
            null,
            null,
            null,
            TrackingTestData.CapturedAt,
            "manual",
            null,
            cancellationToken);

        var conflict = await Assert.ThrowsAsync<TrackingException>(() =>
            fixture.Metrics.CreateAsync(
                fixture.ProjectId,
                200,
                null,
                null,
                null,
                null,
                null,
                TrackingTestData.CapturedAt,
                "manual",
                null,
                cancellationToken));

        Assert.Equal("metric_snapshot_already_recorded", conflict.ErrorCode);
        Assert.Equal(1, fixture.Tracking.SaveCount);
        Assert.Single(fixture.Tracking.Snapshots);
    }

    [Fact]
    public async Task CreateMetric_RejectsInvalidValuesWithSpecificCodes()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertCode(
            "metric_snapshot_views_invalid",
            () => fixture.Metrics.CreateAsync(
                fixture.ProjectId,
                -1,
                null,
                null,
                null,
                null,
                null,
                TrackingTestData.CapturedAt,
                "manual",
                null,
                cancellationToken));
        await AssertCode(
            "metric_snapshot_click_through_rate_invalid",
            () => fixture.Metrics.CreateAsync(
                fixture.ProjectId,
                null,
                null,
                1.5m,
                null,
                null,
                null,
                TrackingTestData.CapturedAt,
                "manual",
                null,
                cancellationToken));
        await AssertCode(
            "metric_snapshot_source_invalid",
            () => fixture.Metrics.CreateAsync(
                fixture.ProjectId,
                1,
                null,
                null,
                null,
                null,
                null,
                TrackingTestData.CapturedAt,
                null,
                null,
                cancellationToken));
        await AssertCode(
            "metric_snapshot_captured_at_invalid",
            () => fixture.Metrics.CreateAsync(
                fixture.ProjectId,
                1,
                null,
                null,
                null,
                null,
                null,
                default,
                "manual",
                null,
                cancellationToken));
        await AssertCode(
            "metric_snapshot_empty",
            () => fixture.Metrics.CreateAsync(
                fixture.ProjectId,
                null,
                null,
                null,
                null,
                null,
                null,
                TrackingTestData.CapturedAt,
                "manual",
                null,
                cancellationToken));

        Assert.Equal(0, fixture.Tracking.SaveCount);
    }

    [Fact]
    public async Task ListMetrics_ReturnsNewestFirstAndRejectsUnboundedWindows()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var hours in new[] { 3, 1, 2 })
        {
            await fixture.Metrics.CreateAsync(
                fixture.ProjectId,
                hours * 10,
                null,
                null,
                null,
                null,
                null,
                TrackingTestData.Now.AddHours(-hours),
                "manual",
                null,
                cancellationToken);
        }

        var snapshots = await fixture.Metrics.ListAsync(
            fixture.ProjectId,
            2,
            cancellationToken);

        Assert.NotNull(snapshots);
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(
            TrackingTestData.Now.AddHours(-1),
            snapshots[0].CapturedAt);
        Assert.Equal(
            TrackingTestData.Now.AddHours(-2),
            snapshots[1].CapturedAt);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Metrics.ListAsync(fixture.ProjectId, 0, cancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Metrics.ListAsync(fixture.ProjectId, 101, cancellationToken));
    }

    [Fact]
    public async Task Summary_UsesTheLatestSnapshotAndNeverSumsSnapshots()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            100,
            null,
            null,
            null,
            null,
            null,
            TrackingTestData.Now.AddHours(-2),
            "manual",
            null,
            cancellationToken);
        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            250,
            null,
            null,
            null,
            null,
            null,
            TrackingTestData.Now.AddHours(-1),
            "manual",
            null,
            cancellationToken);

        var summary = await fixture.Summary.GetAsync(fixture.ProjectId, cancellationToken);

        Assert.NotNull(summary);
        Assert.NotNull(summary.LatestMetricSnapshot);
        Assert.Equal(250, summary.LatestMetricSnapshot.Views);
        Assert.Equal(
            TrackingTestData.Now.AddHours(-1),
            summary.LatestMetricSnapshot.CapturedAt);
    }

    [Fact]
    public async Task Summary_KeepsHumanAndMachineMinutesSeparate()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.TimeEntries.CreateAsync(
            fixture.ProjectId,
            "human",
            "content",
            90m,
            TrackingTestData.Now.AddHours(-3),
            null,
            cancellationToken);
        await fixture.TimeEntries.CreateAsync(
            fixture.ProjectId,
            "human",
            "review",
            30m,
            TrackingTestData.Now.AddHours(-2),
            null,
            cancellationToken);
        await fixture.TimeEntries.CreateAsync(
            fixture.ProjectId,
            "machine",
            "development",
            12.5m,
            TrackingTestData.Now.AddHours(-1),
            null,
            cancellationToken);

        var summary = await fixture.Summary.GetAsync(fixture.ProjectId, cancellationToken);

        Assert.NotNull(summary);
        Assert.Equal(120m, summary.HumanMinutesTotal);
        Assert.Equal(12.5m, summary.MachineMinutesTotal);
    }

    [Fact]
    public async Task Summary_ReportsCostsPerCurrencyWithKindsSeparate()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Costs.CreateAsync(
            fixture.ProjectId,
            150_000m,
            "idr",
            "actual",
            "production",
            TrackingTestData.Now.AddHours(-3),
            null,
            cancellationToken);
        await fixture.Costs.CreateAsync(
            fixture.ProjectId,
            50_000m,
            "IDR",
            "refunded",
            "production",
            TrackingTestData.Now.AddHours(-2),
            null,
            cancellationToken);
        await fixture.Costs.CreateAsync(
            fixture.ProjectId,
            3.5m,
            "usd",
            "actual",
            "hosting",
            TrackingTestData.Now.AddHours(-1),
            null,
            cancellationToken);
        await fixture.Costs.CreateAsync(
            fixture.ProjectId,
            10m,
            "USD",
            "estimated",
            "hosting",
            TrackingTestData.Now,
            null,
            cancellationToken);

        var summary = await fixture.Summary.GetAsync(fixture.ProjectId, cancellationToken);

        Assert.NotNull(summary);
        Assert.Equal(2, summary.Costs.Count);
        var idr = summary.Costs.Single(cost => cost.Currency == "IDR");
        Assert.Equal(150_000m, idr.ActualTotal);
        Assert.Equal(0m, idr.EstimatedTotal);
        Assert.Equal(50_000m, idr.RefundedTotal);
        Assert.Equal(100_000m, idr.NetActualTotal);
        Assert.False(idr.RefundExceedsActual);

        var usd = summary.Costs.Single(cost => cost.Currency == "USD");
        Assert.Equal(3.5m, usd.ActualTotal);
        Assert.Equal(10m, usd.EstimatedTotal);
        Assert.Equal(0m, usd.RefundedTotal);
        Assert.Equal(3.5m, usd.NetActualTotal);
        Assert.False(usd.RefundExceedsActual);
    }

    [Fact]
    public async Task Summary_MarksARefundThatExceedsRecordedActuals()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Costs.CreateAsync(
            fixture.ProjectId,
            10_000m,
            "IDR",
            "actual",
            "production",
            TrackingTestData.Now.AddHours(-2),
            null,
            cancellationToken);
        await fixture.Costs.CreateAsync(
            fixture.ProjectId,
            15_000m,
            "IDR",
            "refunded",
            "production",
            TrackingTestData.Now.AddHours(-1),
            null,
            cancellationToken);

        var summary = await fixture.Summary.GetAsync(fixture.ProjectId, cancellationToken);

        Assert.NotNull(summary);
        var idr = Assert.Single(summary.Costs);
        Assert.Equal(-5_000m, idr.NetActualTotal);
        Assert.True(idr.RefundExceedsActual);
    }

    [Fact]
    public async Task Summary_IsEmptyButKnownForAProjectWithoutTrackingRows()
    {
        var fixture = new Fixture();

        var summary = await fixture.Summary.GetAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(summary);
        Assert.Null(summary.LatestMetricSnapshot);
        Assert.Equal(0m, summary.HumanMinutesTotal);
        Assert.Equal(0m, summary.MachineMinutesTotal);
        Assert.Empty(summary.Costs);
    }

    [Fact]
    public async Task Summary_ReturnsNullWhenTheProjectDoesNotExist()
    {
        var fixture = new Fixture(projectExists: false);

        Assert.Null(await fixture.Summary.GetAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken));
        Assert.Null(await fixture.Metrics.ListAsync(
            fixture.ProjectId,
            20,
            TestContext.Current.CancellationToken));
        Assert.Null(await fixture.TimeEntries.ListAsync(
            fixture.ProjectId,
            20,
            TestContext.Current.CancellationToken));
        Assert.Null(await fixture.Costs.ListAsync(
            fixture.ProjectId,
            20,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateTimeEntry_RejectsInvalidValuesWithSpecificCodes()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertCode(
            "time_entry_kind_invalid",
            () => fixture.TimeEntries.CreateAsync(
                fixture.ProjectId,
                "robot",
                "content",
                30m,
                TrackingTestData.Now,
                null,
                cancellationToken));
        await AssertCode(
            "time_entry_minutes_invalid",
            () => fixture.TimeEntries.CreateAsync(
                fixture.ProjectId,
                "human",
                "content",
                0m,
                TrackingTestData.Now,
                null,
                cancellationToken));
        await AssertCode(
            "time_entry_category_invalid",
            () => fixture.TimeEntries.CreateAsync(
                fixture.ProjectId,
                "machine",
                string.Empty,
                30m,
                TrackingTestData.Now,
                null,
                cancellationToken));
        await AssertCode(
            "time_entry_recorded_at_invalid",
            () => fixture.TimeEntries.CreateAsync(
                fixture.ProjectId,
                "machine",
                "development",
                30m,
                default,
                null,
                cancellationToken));
    }

    [Fact]
    public async Task CreateCostEntry_RejectsInvalidValuesWithSpecificCodes()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        await AssertCode(
            "cost_entry_amount_invalid",
            () => fixture.Costs.CreateAsync(
                fixture.ProjectId,
                0m,
                "IDR",
                "actual",
                "production",
                TrackingTestData.Now,
                null,
                cancellationToken));
        await AssertCode(
            "cost_entry_currency_invalid",
            () => fixture.Costs.CreateAsync(
                fixture.ProjectId,
                10m,
                "RP",
                "actual",
                "production",
                TrackingTestData.Now,
                null,
                cancellationToken));
        await AssertCode(
            "cost_entry_kind_invalid",
            () => fixture.Costs.CreateAsync(
                fixture.ProjectId,
                10m,
                "IDR",
                "paid",
                "production",
                TrackingTestData.Now,
                null,
                cancellationToken));
        await AssertCode(
            "cost_entry_category_invalid",
            () => fixture.Costs.CreateAsync(
                fixture.ProjectId,
                10m,
                "IDR",
                "actual",
                "production cost",
                TrackingTestData.Now,
                null,
                cancellationToken));
    }

    private static async Task AssertCode(
        string expected,
        Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<TrackingException>(action);
        Assert.Equal(expected, exception.ErrorCode);
    }

    private sealed class Fixture
    {
        public Fixture(Guid? publicationRecordId = null, bool projectExists = true)
        {
            var project = TrackingTestData.Project(ProjectId);
            var reader = new ProjectReaderStub(projectExists ? project : null);
            var lookup = new PublicationLookupStub(publicationRecordId);
            var time = new FixedTrackingTimeProvider(TrackingTestData.Now);

            Metrics = new MetricSnapshotWorkflow(Tracking, reader, lookup, time);
            TimeEntries = new TimeEntryWorkflow(Tracking, reader, time);
            Costs = new CostEntryWorkflow(Tracking, reader, time);
            Summary = new TrackingSummaryWorkflow(Tracking, reader);
        }

        public Guid ProjectId { get; } = Guid.NewGuid();

        public FakeTrackingRepository Tracking { get; } = new();

        public MetricSnapshotWorkflow Metrics { get; }

        public TimeEntryWorkflow TimeEntries { get; }

        public CostEntryWorkflow Costs { get; }

        public TrackingSummaryWorkflow Summary { get; }
    }
}
