using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Content;
using AIStudio.Application.Tracking;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace AIStudio.Tests.Tracking;

public sealed class TrackingRollupTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Summary_ReturnsEmptyRollupsWhenNoSnapshotsRecorded()
    {
        var fixture = new Fixture();
        var summary = await fixture.Summary.GetAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(summary);
        Assert.Empty(summary.PublicationRollups);
        Assert.Null(summary.LatestMetricSnapshot);
    }

    [Fact]
    public async Task Summary_RollsUpSingleBoundPublicationWithLatestCapture()
    {
        var publicationId = Guid.NewGuid();
        var fixture = new Fixture(publicationRecordId: publicationId);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Capture 1: earlier
        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 100,
            impressions: 1000,
            clickThroughRate: 0.10m,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: 2,
            capturedAt: TrackingTestData.Now.AddHours(-2),
            source: "manual",
            notes: "early",
            cancellationToken);

        // Capture 2: latest
        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 250,
            impressions: 2000,
            clickThroughRate: 0.125m,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: 5,
            capturedAt: TrackingTestData.Now.AddHours(-1),
            source: "manual",
            notes: "latest",
            cancellationToken);

        var summary = await fixture.Summary.GetAsync(fixture.ProjectId, cancellationToken);

        Assert.NotNull(summary);
        Assert.Single(summary.PublicationRollups);

        var rollup = summary.PublicationRollups[0];
        Assert.Equal(publicationId, rollup.PublicationRecordId);
        Assert.True(rollup.IsBound);
        Assert.Equal(2, rollup.SnapshotCount);
        Assert.NotNull(rollup.LatestMetricSnapshot);
        Assert.Equal(250, rollup.LatestMetricSnapshot.Views);
        Assert.Equal(2000, rollup.LatestMetricSnapshot.Impressions);
        Assert.Equal(0.125m, rollup.LatestMetricSnapshot.ClickThroughRate);
        Assert.Equal(TrackingTestData.Now.AddHours(-1), rollup.LatestMetricSnapshot.CapturedAt);
    }

    [Fact]
    public async Task Summary_RollsUpUnboundSnapshotsUnderNullPublicationId()
    {
        // No publication bound
        var fixture = new Fixture(publicationRecordId: null);
        var cancellationToken = TestContext.Current.CancellationToken;

        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 50,
            impressions: null,
            clickThroughRate: null,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: null,
            capturedAt: TrackingTestData.Now.AddHours(-3),
            source: "manual",
            notes: "unbound 1",
            cancellationToken);

        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 80,
            impressions: null,
            clickThroughRate: null,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: null,
            capturedAt: TrackingTestData.Now.AddHours(-1),
            source: "manual",
            notes: "unbound 2",
            cancellationToken);

        var summary = await fixture.Summary.GetAsync(fixture.ProjectId, cancellationToken);

        Assert.NotNull(summary);
        Assert.Single(summary.PublicationRollups);

        var rollup = summary.PublicationRollups[0];
        Assert.Null(rollup.PublicationRecordId);
        Assert.False(rollup.IsBound);
        Assert.Equal(2, rollup.SnapshotCount);
        Assert.NotNull(rollup.LatestMetricSnapshot);
        Assert.Equal(80, rollup.LatestMetricSnapshot.Views);
    }

    [Fact]
    public async Task Summary_SeparatesMultiplePublicationsAndUnboundObservations()
    {
        var pubA = Guid.NewGuid();
        var pubB = Guid.NewGuid();
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Unbound snapshot
        fixture.SetPublication(null);
        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 10,
            impressions: null,
            clickThroughRate: null,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: null,
            capturedAt: TrackingTestData.Now.AddHours(-5),
            source: "manual",
            notes: "unbound",
            cancellationToken);

        // Publication A: 2 captures
        fixture.SetPublication(pubA);
        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 100,
            impressions: 500,
            clickThroughRate: null,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: null,
            capturedAt: TrackingTestData.Now.AddHours(-4),
            source: "manual",
            notes: "pubA initial",
            cancellationToken);
        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 150,
            impressions: 700,
            clickThroughRate: null,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: null,
            capturedAt: TrackingTestData.Now.AddHours(-2),
            source: "manual",
            notes: "pubA latest",
            cancellationToken);

        // Publication B: 1 capture (latest overall)
        fixture.SetPublication(pubB);
        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 800,
            impressions: 4000,
            clickThroughRate: null,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: null,
            capturedAt: TrackingTestData.Now.AddHours(-1),
            source: "manual",
            notes: "pubB capture",
            cancellationToken);

        var summary = await fixture.Summary.GetAsync(fixture.ProjectId, cancellationToken);

        Assert.NotNull(summary);
        Assert.Equal(3, summary.PublicationRollups.Count);

        // Overall latest should be pubB (captured at -1 hour)
        Assert.NotNull(summary.LatestMetricSnapshot);
        Assert.Equal(pubB, summary.LatestMetricSnapshot.PublicationRecordId);
        Assert.Equal(800, summary.LatestMetricSnapshot.Views);

        // Rollup for pubA
        var rollupA = summary.PublicationRollups.Single(r => r.PublicationRecordId == pubA);
        Assert.True(rollupA.IsBound);
        Assert.Equal(2, rollupA.SnapshotCount);
        Assert.Equal(150, rollupA.LatestMetricSnapshot?.Views);

        // Rollup for pubB
        var rollupB = summary.PublicationRollups.Single(r => r.PublicationRecordId == pubB);
        Assert.True(rollupB.IsBound);
        Assert.Equal(1, rollupB.SnapshotCount);
        Assert.Equal(800, rollupB.LatestMetricSnapshot?.Views);

        // Rollup for unbound
        var rollupUnbound = summary.PublicationRollups.Single(r => r.PublicationRecordId is null);
        Assert.False(rollupUnbound.IsBound);
        Assert.Equal(1, rollupUnbound.SnapshotCount);
        Assert.Equal(10, rollupUnbound.LatestMetricSnapshot?.Views);
    }

    [Fact]
    public async Task Summary_PreservesTimeAndCostTotalsAlongsideRollups()
    {
        var pubId = Guid.NewGuid();
        var fixture = new Fixture(publicationRecordId: pubId);
        var cancellationToken = TestContext.Current.CancellationToken;

        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 300,
            impressions: null,
            clickThroughRate: null,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: null,
            capturedAt: TrackingTestData.Now.AddHours(-1),
            source: "manual",
            notes: null,
            cancellationToken);

        await fixture.TimeEntries.CreateAsync(
            fixture.ProjectId,
            "human",
            "editing",
            60m,
            TrackingTestData.Now.AddHours(-2),
            null,
            cancellationToken);

        await fixture.TimeEntries.CreateAsync(
            fixture.ProjectId,
            "machine",
            "rendering",
            15.5m,
            TrackingTestData.Now.AddHours(-2),
            null,
            cancellationToken);

        await fixture.Costs.CreateAsync(
            fixture.ProjectId,
            50_000m,
            "IDR",
            "actual",
            "hosting",
            TrackingTestData.Now.AddHours(-1),
            null,
            cancellationToken);

        var summary = await fixture.Summary.GetAsync(fixture.ProjectId, cancellationToken);

        Assert.NotNull(summary);
        Assert.Single(summary.PublicationRollups);
        Assert.Equal(60m, summary.HumanMinutesTotal);
        Assert.Equal(15.5m, summary.MachineMinutesTotal);
        Assert.Single(summary.Costs);
        Assert.Equal("IDR", summary.Costs[0].Currency);
        Assert.Equal(50_000m, summary.Costs[0].ActualTotal);
    }

    [Fact]
    public async Task TrackingEndpoints_GetSummary_SerializesPublicationRollupsInJson()
    {
        var pubId = Guid.NewGuid();
        var fixture = new Fixture(publicationRecordId: pubId);
        var cancellationToken = TestContext.Current.CancellationToken;

        await fixture.Metrics.CreateAsync(
            fixture.ProjectId,
            views: 1234,
            impressions: 5678,
            clickThroughRate: 0.08m,
            averageViewDurationSeconds: null,
            watchTimeMinutes: null,
            subscribersGained: null,
            capturedAt: TrackingTestData.Now.AddHours(-1),
            source: "manual",
            notes: null,
            cancellationToken);

        var result = await TrackingEndpoints.GetTrackingSummaryAsync(
            fixture.ProjectId.ToString(),
            fixture.Summary,
            cancellationToken);

        var ok = Assert.IsType<Ok<TrackingSummaryResponse>>(result);
        Assert.NotNull(ok.Value);
        Assert.Single(ok.Value.PublicationRollups);

        var json = JsonSerializer.SerializeToElement(ok.Value, WebJson);
        Assert.True(json.TryGetProperty("publicationRollups", out var rollupsElement));
        Assert.Equal(1, rollupsElement.GetArrayLength());

        var first = rollupsElement[0];
        Assert.Equal(pubId.ToString(), first.GetProperty("publicationRecordId").GetString());
        Assert.Equal(1, first.GetProperty("snapshotCount").GetInt32());
        Assert.Equal(1234, first.GetProperty("latestMetricSnapshot").GetProperty("views").GetInt32());
    }

    private sealed class Fixture
    {
        private readonly MutablePublicationLookup lookup;

        public Fixture(Guid? publicationRecordId = null)
        {
            var project = TrackingTestData.Project(ProjectId);
            var reader = new ProjectReaderStub(project);
            lookup = new MutablePublicationLookup(publicationRecordId);
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

        public void SetPublication(Guid? publicationRecordId) =>
            lookup.PublicationRecordId = publicationRecordId;
    }

    private sealed class MutablePublicationLookup(Guid? publicationRecordId) : IPublicationReferenceLookup
    {
        public Guid? PublicationRecordId { get; set; } = publicationRecordId;

        public Task<Guid?> FindPublicationRecordIdAsync(
            Guid contentProjectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(PublicationRecordId);
    }
}
