using AIStudio.Domain.Tracking;
using Xunit;

namespace AIStudio.Tests.Tracking;

public sealed class TimeEntryTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public void Create_RecordsHumanAndMachineWorkSeparately()
    {
        var human = TimeEntry.Create(
            ProjectId,
            TimeEntryKind.Human,
            "content",
            90m,
            TrackingTestData.Now,
            "script rewrite",
            TrackingTestData.Now);
        var machine = TimeEntry.Create(
            ProjectId,
            TimeEntryKind.Machine,
            "development",
            12.5m,
            TrackingTestData.Now,
            null,
            TrackingTestData.Now);

        Assert.Equal(TimeEntryKind.Human, human.Kind);
        Assert.Equal(90m, human.Minutes);
        Assert.Equal("script rewrite", human.Notes);
        Assert.Equal(TimeEntryKind.Machine, machine.Kind);
        Assert.Equal(12.5m, machine.Minutes);
        Assert.NotEqual(human.Kind, machine.Kind);
    }

    [Fact]
    public void Create_RejectsNonPositiveMinutes()
    {
        Assert.Equal("minutes", Record(() => Create(minutes: 0m)).ParamName);
        Assert.Equal("minutes", Record(() => Create(minutes: -5m)).ParamName);
        Assert.Equal(
            "minutes",
            Record(() => Create(minutes: TrackingRules.MaxMinutesPerEntry + 1m)).ParamName);
    }

    [Fact]
    public void Create_NormalizesTheCategoryToken()
    {
        Assert.Equal("review", Create(category: " REVIEW ").Category);
        Assert.Equal("asset-collection", Create(category: "asset-collection").Category);

        Assert.Equal("category", Record(() => Create(category: "content review")).ParamName);
        Assert.Equal("category", Record(() => Create(category: "1content")).ParamName);
        Assert.Equal("category", Record(() => Create(category: string.Empty)).ParamName);
    }

    [Fact]
    public void Create_NormalizesRecordedAtToUtc()
    {
        var recordedAt = new DateTimeOffset(2026, 11, 3, 9, 15, 0, TimeSpan.FromHours(7));

        var entry = Create(recordedAt: recordedAt);

        Assert.Equal(TimeSpan.Zero, entry.RecordedAt.Offset);
        Assert.Equal(recordedAt.ToUniversalTime(), entry.RecordedAt);
    }

    [Fact]
    public void Create_RejectsADefaultRecordedAt()
    {
        Assert.Equal("recordedAt", Record(() => Create(recordedAt: DateTimeOffset.MinValue)).ParamName);
    }

    [Fact]
    public void Create_RejectsAnUndefinedKind()
    {
        Assert.Equal(
            "kind",
            Record(() => Create(kind: (TimeEntryKind)99)).ParamName);
    }

    [Theory]
    [InlineData("human", TimeEntryKind.Human)]
    [InlineData("HUMAN", TimeEntryKind.Human)]
    [InlineData(" machine ", TimeEntryKind.Machine)]
    [InlineData("Machine", TimeEntryKind.Machine)]
    public void KindTokens_ParseKnownValues(string token, TimeEntryKind expected)
    {
        Assert.True(TrackingKinds.TryParseTimeEntryKind(token, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("bot")]
    [InlineData("human-machine")]
    [InlineData("")]
    [InlineData(null)]
    public void KindTokens_RejectUnknownValues(string? token)
    {
        Assert.False(TrackingKinds.TryParseTimeEntryKind(token, out _));
    }

    [Fact]
    public void KindTokens_RenderLowercase()
    {
        Assert.Equal("human", TrackingKinds.ToToken(TimeEntryKind.Human));
        Assert.Equal("machine", TrackingKinds.ToToken(TimeEntryKind.Machine));
    }

    private static TimeEntry Create(
        TimeEntryKind kind = TimeEntryKind.Human,
        string category = "content",
        decimal minutes = 30m,
        DateTimeOffset? recordedAt = null) =>
        TimeEntry.Create(
            ProjectId,
            kind,
            category,
            minutes,
            recordedAt ?? TrackingTestData.Now,
            null,
            TrackingTestData.Now);

    private static ArgumentException Record(Func<TimeEntry> create) =>
        Assert.ThrowsAny<ArgumentException>(create);
}
