using AIStudio.Domain.Tracking;
using Xunit;

namespace AIStudio.Tests.Tracking;

public sealed class MetricSnapshotTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public void Create_KeepsNullAsUnknownAndZeroAsMeasured()
    {
        var snapshot = MetricSnapshot.Create(
            ProjectId,
            publicationRecordId: null,
            views: 0,
            impressions: null,
            clickThroughRate: null,
            averageViewDurationSeconds: null,
            watchTimeMinutes: 0m,
            subscribersGained: null,
            TrackingTestData.CapturedAt,
            "manual",
            null,
            TrackingTestData.Now);

        Assert.Equal(0, snapshot.Views);
        Assert.Null(snapshot.Impressions);
        Assert.Null(snapshot.ClickThroughRate);
        Assert.Null(snapshot.AverageViewDurationSeconds);
        Assert.Equal(0m, snapshot.WatchTimeMinutes);
        Assert.Null(snapshot.SubscribersGained);
        Assert.Equal(TrackingTestData.Now, snapshot.CreatedAt);
    }

    [Fact]
    public void Create_RejectsNegativeValues()
    {
        Assert.Equal("views", Record(() => Create(views: -1)).ParamName);
        Assert.Equal("impressions", Record(() => Create(impressions: -1)).ParamName);
        Assert.Equal(
            "averageViewDurationSeconds",
            Record(() => Create(averageViewDurationSeconds: -0.5m)).ParamName);
        Assert.Equal(
            "watchTimeMinutes",
            Record(() => Create(watchTimeMinutes: -1m)).ParamName);
        Assert.Equal(
            "subscribersGained",
            Record(() => Create(subscribersGained: -3)).ParamName);
    }

    [Fact]
    public void Create_AcceptsOnlyRatesBetweenZeroAndOne()
    {
        Assert.Equal(0m, Create(clickThroughRate: 0m).ClickThroughRate);
        Assert.Equal(1m, Create(clickThroughRate: 1m).ClickThroughRate);

        Assert.Equal(
            "clickThroughRate",
            Record(() => Create(clickThroughRate: -0.0001m)).ParamName);
        Assert.Equal(
            "clickThroughRate",
            Record(() => Create(clickThroughRate: 1.0001m)).ParamName);
    }

    [Fact]
    public void Create_NormalizesCapturedAtToUtc()
    {
        var capturedAt = new DateTimeOffset(
            2026,
            11,
            3,
            17,
            30,
            0,
            TimeSpan.FromHours(7));

        var snapshot = Create(capturedAt: capturedAt);

        Assert.Equal(TimeSpan.Zero, snapshot.CapturedAt.Offset);
        Assert.Equal(capturedAt.ToUniversalTime(), snapshot.CapturedAt);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 30, 0, DateTimeKind.Utc), snapshot.CapturedAt.UtcDateTime);
    }

    [Fact]
    public void Create_RejectsADefaultCaptureInstant()
    {
        Assert.Equal(
            "capturedAt",
            Record(() => Create(capturedAt: DateTimeOffset.MinValue)).ParamName);
    }

    [Fact]
    public void Create_NormalizesTheSourceToken()
    {
        Assert.Equal("manual", Create(source: "  MANUAL  ").Source);
        Assert.Equal("youtube-analytics-export", Create(source: " YouTube-Analytics-Export ").Source);

        Assert.Equal("source", Record(() => Create(source: "manual capture")).ParamName);
        Assert.Equal("source", Record(() => Create(source: "1manual")).ParamName);
        Assert.Equal("source", Record(() => Create(source: "manual!")).ParamName);
        Assert.Equal("source", Record(() => Create(source: " ")).ParamName);
    }

    [Fact]
    public void Create_RejectsASnapshotWithoutAnyMetric()
    {
        var exception = Record(() => MetricSnapshot.Create(
            ProjectId,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            TrackingTestData.CapturedAt,
            "manual",
            null,
            TrackingTestData.Now));

        Assert.Equal("metrics", exception.ParamName);
    }

    [Fact]
    public void Create_RejectsAnEmptyProjectIdentifier()
    {
        Assert.Equal(
            "contentProjectId",
            Record(() => MetricSnapshot.Create(
                Guid.Empty,
                null,
                views: 1,
                impressions: null,
                clickThroughRate: null,
                averageViewDurationSeconds: null,
                watchTimeMinutes: null,
                subscribersGained: null,
                TrackingTestData.CapturedAt,
                "manual",
                null,
                TrackingTestData.Now)).ParamName);
    }

    [Fact]
    public void Create_TrimsNotesAndRejectsOverlongNotes()
    {
        Assert.Equal("first capture", Create(notes: "  first capture  ").Notes);
        Assert.Null(Create(notes: "   ").Notes);

        var exception = Record(() => Create(notes: new string('n', TrackingRules.MaxNotesLength + 1)));
        Assert.Equal("notes", exception.ParamName);
    }

    [Fact]
    public void Create_KeepsThePublicationBindingWhenSupplied()
    {
        var publicationRecordId = Guid.NewGuid();

        var snapshot = MetricSnapshot.Create(
            ProjectId,
            publicationRecordId,
            views: 10,
            impressions: 100,
            clickThroughRate: 0.1m,
            averageViewDurationSeconds: 42.5m,
            watchTimeMinutes: 7.083m,
            subscribersGained: 2,
            TrackingTestData.CapturedAt,
            "manual",
            null,
            TrackingTestData.Now);

        Assert.Equal(publicationRecordId, snapshot.PublicationRecordId);
        Assert.Equal(42.5m, snapshot.AverageViewDurationSeconds);
        Assert.Equal(7.083m, snapshot.WatchTimeMinutes);
    }

    private static MetricSnapshot Create(
        int? views = 1,
        int? impressions = null,
        decimal? clickThroughRate = null,
        decimal? averageViewDurationSeconds = null,
        decimal? watchTimeMinutes = null,
        int? subscribersGained = null,
        DateTimeOffset? capturedAt = null,
        string? source = "manual",
        string? notes = null) =>
        MetricSnapshot.Create(
            ProjectId,
            null,
            views,
            impressions,
            clickThroughRate,
            averageViewDurationSeconds,
            watchTimeMinutes,
            subscribersGained,
            capturedAt ?? TrackingTestData.CapturedAt,
            source ?? string.Empty,
            notes,
            TrackingTestData.Now);

    private static ArgumentException Record(Func<MetricSnapshot> create) =>
        Assert.ThrowsAny<ArgumentException>(create);
}
