using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Content;
using AIStudio.Application.Tracking;
using AIStudio.Tests.Tracking;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace AIStudio.Tests.Api;

public sealed class TrackingApiContractTests
{
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateMetric_ReturnsCreatedAndPreservesNullVersusZero()
    {
        var fixture = new ApiFixture();

        var result = await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            new CreateMetricSnapshotRequest(
                Views: 0,
                Impressions: null,
                ClickThroughRate: 0.05m,
                AverageViewDurationSeconds: null,
                WatchTimeMinutes: null,
                SubscribersGained: 4,
                CapturedAt: TrackingTestData.CapturedAt,
                Source: "manual",
                Notes: "first capture"),
            fixture.Metrics,
            TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<MetricSnapshotResponse>>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.NotNull(created.Value);
        Assert.Equal(0, created.Value.Views);
        Assert.Null(created.Value.Impressions);

        var json = JsonSerializer.SerializeToElement(created.Value, WebJson);
        Assert.Equal(JsonValueKind.Number, json.GetProperty("views").ValueKind);
        Assert.Equal(0, json.GetProperty("views").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("impressions").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("watchTimeMinutes").ValueKind);
        Assert.Equal(0.05m, json.GetProperty("clickThroughRate").GetDecimal());
        Assert.Equal("manual", json.GetProperty("source").GetString());
        Assert.False(json.TryGetProperty("totalViews", out _));
        Assert.False(json.TryGetProperty("viewsSum", out _));
        Assert.False(json.TryGetProperty("watchTimeMinutesTotal", out _));
    }

    [Fact]
    public async Task CreateMetric_ReturnsValidationProblemsWithSpecificCodes()
    {
        var fixture = new ApiFixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        var negativeViews = await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            MetricRequest(views: -5),
            fixture.Metrics,
            cancellationToken);
        AssertProblemCode(negativeViews, 400, "metric_snapshot_views_invalid");

        var rate = await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            MetricRequest(clickThroughRate: 1.5m),
            fixture.Metrics,
            cancellationToken);
        AssertProblemCode(rate, 400, "metric_snapshot_click_through_rate_invalid");

        var source = await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            MetricRequest(source: "manual capture"),
            fixture.Metrics,
            cancellationToken);
        AssertProblemCode(source, 400, "metric_snapshot_source_invalid");

        var capturedAt = await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            new CreateMetricSnapshotRequest(
                10,
                Impressions: null,
                ClickThroughRate: null,
                AverageViewDurationSeconds: null,
                WatchTimeMinutes: null,
                SubscribersGained: null,
                CapturedAt: default,
                Source: "manual",
                Notes: null),
            fixture.Metrics,
            cancellationToken);
        AssertProblemCode(capturedAt, 400, "metric_snapshot_captured_at_invalid");
    }

    [Fact]
    public async Task CreateMetric_ReturnsConflictForASecondCaptureAtTheSameInstant()
    {
        var fixture = new ApiFixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            MetricRequest(),
            fixture.Metrics,
            cancellationToken);

        var result = await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            MetricRequest(views: 999),
            fixture.Metrics,
            cancellationToken);

        AssertProblemCode(result, 409, "metric_snapshot_already_recorded");
    }

    [Fact]
    public async Task Tracking_ReturnsNotFoundForAnUnknownProject()
    {
        var fixture = new ApiFixture(projectExists: false);
        var cancellationToken = TestContext.Current.CancellationToken;

        var metric = await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            MetricRequest(),
            fixture.Metrics,
            cancellationToken);
        AssertProblemCode(metric, 404, "content_project_not_found");

        var summary = await TrackingEndpoints.GetTrackingSummaryAsync(
            fixture.ProjectId.ToString(),
            fixture.Summary,
            cancellationToken);
        AssertProblemCode(summary, 404, "content_project_not_found");

        var list = await TrackingEndpoints.ListCostEntriesAsync(
            fixture.ProjectId.ToString(),
            null,
            fixture.Costs,
            cancellationToken);
        AssertProblemCode(list, 404, "content_project_not_found");
    }

    [Fact]
    public async Task CreateTimeEntry_ReturnsLowercaseKindAndRejectsUnknownKinds()
    {
        var fixture = new ApiFixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        var created = await TrackingEndpoints.CreateTimeEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateTimeEntryRequest(
                "MACHINE",
                "development",
                45.5m,
                TrackingTestData.Now,
                "build"),
            fixture.TimeEntries,
            cancellationToken);

        var result = Assert.IsType<Created<TimeEntryResponse>>(created);
        Assert.Equal(201, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("machine", result.Value.Kind);
        Assert.Equal(45.5m, result.Value.Minutes);

        var invalid = await TrackingEndpoints.CreateTimeEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateTimeEntryRequest("robot", "development", 10m, TrackingTestData.Now, null),
            fixture.TimeEntries,
            cancellationToken);
        AssertProblemCode(invalid, 400, "time_entry_kind_invalid");

        var zero = await TrackingEndpoints.CreateTimeEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateTimeEntryRequest("human", "content", 0m, TrackingTestData.Now, null),
            fixture.TimeEntries,
            cancellationToken);
        AssertProblemCode(zero, 400, "time_entry_minutes_invalid");
    }

    [Fact]
    public async Task CreateCostEntry_ReturnsCanonicalCurrencyAndRejectsInvalidMoney()
    {
        var fixture = new ApiFixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        var created = await TrackingEndpoints.CreateCostEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateCostEntryRequest(
                25_000m,
                "idr",
                "refunded",
                "production",
                TrackingTestData.Now,
                null),
            fixture.Costs,
            cancellationToken);

        var result = Assert.IsType<Created<CostEntryResponse>>(created);
        Assert.Equal(201, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal("IDR", result.Value.Currency);
        Assert.Equal("refunded", result.Value.Kind);

        var currency = await TrackingEndpoints.CreateCostEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateCostEntryRequest(10m, "RP", "actual", "production", TrackingTestData.Now, null),
            fixture.Costs,
            cancellationToken);
        AssertProblemCode(currency, 400, "cost_entry_currency_invalid");

        var amount = await TrackingEndpoints.CreateCostEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateCostEntryRequest(0m, "IDR", "actual", "production", TrackingTestData.Now, null),
            fixture.Costs,
            cancellationToken);
        AssertProblemCode(amount, 400, "cost_entry_amount_invalid");
    }

    [Fact]
    public async Task GetSummary_ReturnsTheLatestSnapshotAndSeparateTotals()
    {
        var fixture = new ApiFixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            MetricRequest(views: 100, capturedAt: TrackingTestData.Now.AddHours(-2)),
            fixture.Metrics,
            cancellationToken);
        await TrackingEndpoints.CreateMetricSnapshotAsync(
            fixture.ProjectId.ToString(),
            MetricRequest(views: 250, capturedAt: TrackingTestData.Now.AddHours(-1)),
            fixture.Metrics,
            cancellationToken);
        await TrackingEndpoints.CreateTimeEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateTimeEntryRequest("human", "content", 90m, TrackingTestData.Now, null),
            fixture.TimeEntries,
            cancellationToken);
        await TrackingEndpoints.CreateTimeEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateTimeEntryRequest("machine", "development", 12.5m, TrackingTestData.Now, null),
            fixture.TimeEntries,
            cancellationToken);
        await TrackingEndpoints.CreateCostEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateCostEntryRequest(150_000m, "IDR", "actual", "production", TrackingTestData.Now, null),
            fixture.Costs,
            cancellationToken);
        await TrackingEndpoints.CreateCostEntryAsync(
            fixture.ProjectId.ToString(),
            new CreateCostEntryRequest(50_000m, "IDR", "refunded", "production", TrackingTestData.Now, null),
            fixture.Costs,
            cancellationToken);

        var result = await TrackingEndpoints.GetTrackingSummaryAsync(
            fixture.ProjectId.ToString(),
            fixture.Summary,
            cancellationToken);

        var ok = Assert.IsType<Ok<TrackingSummaryResponse>>(result);
        Assert.NotNull(ok.Value);

        var json = JsonSerializer.SerializeToElement(ok.Value, WebJson);
        var latest = json.GetProperty("latestMetricSnapshot");
        Assert.Equal(250, latest.GetProperty("views").GetInt32());
        Assert.Equal(90m, json.GetProperty("humanMinutesTotal").GetDecimal());
        Assert.Equal(12.5m, json.GetProperty("machineMinutesTotal").GetDecimal());
        Assert.False(json.TryGetProperty("totalMinutes", out _));
        Assert.False(json.TryGetProperty("viewsTotal", out _));

        var costs = json.GetProperty("costs");
        Assert.Equal(1, costs.GetArrayLength());
        Assert.Equal("IDR", costs[0].GetProperty("currency").GetString());
        Assert.Equal(150_000m, costs[0].GetProperty("actualTotal").GetDecimal());
        Assert.Equal(0m, costs[0].GetProperty("estimatedTotal").GetDecimal());
        Assert.Equal(50_000m, costs[0].GetProperty("refundedTotal").GetDecimal());
        Assert.Equal(100_000m, costs[0].GetProperty("netActualTotal").GetDecimal());
        Assert.False(costs[0].GetProperty("refundExceedsActual").GetBoolean());
    }

    [Fact]
    public async Task List_RejectsAnUnboundedTake()
    {
        var fixture = new ApiFixture();

        var result = await TrackingEndpoints.ListMetricSnapshotsAsync(
            fixture.ProjectId.ToString(),
            500,
            fixture.Metrics,
            TestContext.Current.CancellationToken);

        AssertProblemCode(result, 400, "metric_snapshot_invalid_request");
    }

    [Fact]
    public async Task CreateMetric_RejectsAMalformedProjectIdentifier()
    {
        var fixture = new ApiFixture();

        var result = await TrackingEndpoints.CreateMetricSnapshotAsync(
            "not-a-guid",
            MetricRequest(),
            fixture.Metrics,
            TestContext.Current.CancellationToken);

        AssertProblemCode(result, 400, "metric_snapshot_invalid_request");
    }

    private static CreateMetricSnapshotRequest MetricRequest(
        int? views = 10,
        decimal? clickThroughRate = null,
        string? source = "manual",
        DateTimeOffset? capturedAt = null) =>
        new(
            views,
            Impressions: null,
            clickThroughRate,
            AverageViewDurationSeconds: null,
            WatchTimeMinutes: null,
            SubscribersGained: null,
            capturedAt ?? TrackingTestData.CapturedAt,
            source,
            Notes: null);

    private static void AssertProblemCode(IResult result, int statusCode, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(statusCode, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    private sealed class ApiFixture
    {
        public ApiFixture(Guid? publicationRecordId = null, bool projectExists = true)
        {
            var project = new ContentProjectSnapshot(
                ProjectId,
                "Tracking API fixture",
                null);
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
