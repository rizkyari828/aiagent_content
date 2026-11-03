using AIStudio.Domain.Tracking;
using Xunit;

namespace AIStudio.Tests.Tracking;

public sealed class CostEntryTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public void Create_RecordsAPositiveAmountInACurrency()
    {
        var entry = Create(amount: 150_000m, currency: "idr");

        Assert.Equal(150_000m, entry.Amount);
        Assert.Equal("IDR", entry.Currency);
        Assert.Equal(CostEntryKind.Actual, entry.Kind);
        Assert.Equal("production", entry.Category);
    }

    [Fact]
    public void Create_RejectsNonPositiveAmounts()
    {
        Assert.Equal("amount", Record(() => Create(amount: 0m)).ParamName);
        Assert.Equal("amount", Record(() => Create(amount: -0.01m)).ParamName);
        Assert.Equal(
            "amount",
            Record(() => Create(amount: TrackingRules.MaxAmount + 1m)).ParamName);
    }

    [Fact]
    public void Create_RequiresAThreeLetterCurrency()
    {
        Assert.Equal("IDR", Create(currency: " idr ").Currency);
        Assert.Equal("USD", Create(currency: "usd").Currency);

        Assert.Equal("currency", Record(() => Create(currency: "ID")).ParamName);
        Assert.Equal("currency", Record(() => Create(currency: "RUPIAH")).ParamName);
        Assert.Equal("currency", Record(() => Create(currency: "U5D")).ParamName);
        Assert.Equal("currency", Record(() => Create(currency: string.Empty)).ParamName);
    }

    [Fact]
    public void Create_KeepsRefundsPositiveWithARefundKind()
    {
        var refund = Create(amount: 25_000m, kind: CostEntryKind.Refunded);

        Assert.Equal(25_000m, refund.Amount);
        Assert.Equal(CostEntryKind.Refunded, refund.Kind);
    }

    [Fact]
    public void Create_NormalizesIncurredAtToUtc()
    {
        var incurredAt = new DateTimeOffset(2026, 11, 3, 8, 45, 0, TimeSpan.FromHours(7));

        var entry = Create(incurredAt: incurredAt);

        Assert.Equal(TimeSpan.Zero, entry.IncurredAt.Offset);
        Assert.Equal(incurredAt.ToUniversalTime(), entry.IncurredAt);
    }

    [Fact]
    public void Create_RejectsADefaultIncurredAt()
    {
        Assert.Equal("incurredAt", Record(() => Create(incurredAt: DateTimeOffset.MinValue)).ParamName);
    }

    [Fact]
    public void Create_RejectsAnUndefinedKind()
    {
        Assert.Equal(
            "kind",
            Record(() => Create(kind: (CostEntryKind)99)).ParamName);
    }

    [Theory]
    [InlineData("actual", CostEntryKind.Actual)]
    [InlineData("ESTIMATED", CostEntryKind.Estimated)]
    [InlineData(" refunded ", CostEntryKind.Refunded)]
    public void KindTokens_ParseKnownValues(string token, CostEntryKind expected)
    {
        Assert.True(TrackingKinds.TryParseCostEntryKind(token, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("paid")]
    [InlineData("")]
    [InlineData(null)]
    public void KindTokens_RejectUnknownValues(string? token)
    {
        Assert.False(TrackingKinds.TryParseCostEntryKind(token, out _));
    }

    [Fact]
    public void KindTokens_RenderLowercase()
    {
        Assert.Equal("actual", TrackingKinds.ToToken(CostEntryKind.Actual));
        Assert.Equal("estimated", TrackingKinds.ToToken(CostEntryKind.Estimated));
        Assert.Equal("refunded", TrackingKinds.ToToken(CostEntryKind.Refunded));
    }

    private static CostEntry Create(
        decimal amount = 10_000m,
        string currency = "IDR",
        CostEntryKind kind = CostEntryKind.Actual,
        string category = "production",
        DateTimeOffset? incurredAt = null) =>
        CostEntry.Create(
            ProjectId,
            amount,
            currency,
            kind,
            category,
            incurredAt ?? TrackingTestData.Now,
            null,
            TrackingTestData.Now);

    private static ArgumentException Record(Func<CostEntry> create) =>
        Assert.ThrowsAny<ArgumentException>(create);
}
