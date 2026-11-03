using AIStudio.Domain.Approvals;
using Xunit;

namespace AIStudio.Tests.Approvals;

/// <summary>
/// Domain contract for the two-way A3 review decision: token defaults, note rules, and
/// the revision/idempotency behavior of <see cref="FinalApproval.Apply"/>.
/// </summary>
public sealed class ReviewDecisionDomainTests
{
    private static readonly DateTimeOffset Now = ApprovalTestData.Now;

    [Fact]
    public void Create_DefaultsToApprovedWithoutNote()
    {
        var approval = ApprovalTestData.CreateApproval(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(FinalApprovalDecision.Approved, approval.Decision);
        Assert.Null(approval.DecisionNote);
        Assert.Equal(Now, approval.DecidedAt);
        Assert.Equal(1, approval.Revision);
    }

    [Fact]
    public void Create_RecordsChangesRequestedWithTrimmedNote()
    {
        var approval = ApprovalTestData.CreateApproval(
            Guid.NewGuid(),
            Guid.NewGuid(),
            decision: FinalApprovalDecision.ChangesRequested,
            decisionNote: "  Subtitles are cut off in scene 3  ");

        Assert.Equal(FinalApprovalDecision.ChangesRequested, approval.Decision);
        Assert.Equal("Subtitles are cut off in scene 3", approval.DecisionNote);
        Assert.Equal(Now, approval.DecidedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsChangesRequestedWithoutNote(string? note)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ApprovalTestData.CreateApproval(
                Guid.NewGuid(),
                Guid.NewGuid(),
                decision: FinalApprovalDecision.ChangesRequested,
                decisionNote: note));

        Assert.Equal("decisionNote", exception.ParamName);
        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_RejectsNotesLongerThanTwoThousandCharacters()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ApprovalTestData.CreateApproval(
                Guid.NewGuid(),
                Guid.NewGuid(),
                decision: FinalApprovalDecision.ChangesRequested,
                decisionNote: new string(
                    'x',
                    FinalApproval.MaxDecisionNoteLength + 1)));

        Assert.Equal("decisionNote", exception.ParamName);
    }

    [Fact]
    public void Create_AcceptsExactlyTwoThousandCharacters()
    {
        var note = new string('x', FinalApproval.MaxDecisionNoteLength);
        var approval = ApprovalTestData.CreateApproval(
            Guid.NewGuid(),
            Guid.NewGuid(),
            decision: FinalApprovalDecision.ChangesRequested,
            decisionNote: note);

        Assert.Equal(note, approval.DecisionNote);
    }

    [Fact]
    public void Create_RejectsUnknownDecisionValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FinalApproval.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ApprovalTestData.RenderPath,
                ApprovalTestData.RenderHash,
                ApprovalTestData.RenderByteSize,
                "youtube",
                null,
                null,
                Now,
                decision: (FinalApprovalDecision)99,
                decisionNote: "unknown"));
    }

    [Fact]
    public void Apply_IsIdempotentForTheSameDecisionAndNote()
    {
        var approval = ApprovalTestData.CreateApproval(
            Guid.NewGuid(),
            Guid.NewGuid(),
            decision: FinalApprovalDecision.ChangesRequested,
            decisionNote: "Fix scene 3");

        var changed = approval.Apply(
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            "youtube",
            null,
            null,
            Now.AddMinutes(5),
            FinalApprovalDecision.ChangesRequested,
            "Fix scene 3");

        Assert.False(changed);
        Assert.Equal(1, approval.Revision);
        Assert.Equal(Now, approval.DecidedAt);
    }

    [Fact]
    public void Apply_IncrementsRevisionAndMovesDecidedAtWhenDecisionChanges()
    {
        var approval = ApprovalTestData.CreateApproval(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(approval.Apply(
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            "youtube",
            null,
            null,
            Now.AddMinutes(2),
            FinalApprovalDecision.ChangesRequested,
            "  Fix the ending  "));

        Assert.Equal(2, approval.Revision);
        Assert.Equal(FinalApprovalDecision.ChangesRequested, approval.Decision);
        Assert.Equal("Fix the ending", approval.DecisionNote);
        Assert.Equal(Now.AddMinutes(2), approval.DecidedAt);

        Assert.True(approval.Apply(
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            "youtube",
            null,
            null,
            Now.AddMinutes(3),
            FinalApprovalDecision.Approved,
            null));

        Assert.Equal(3, approval.Revision);
        Assert.Equal(FinalApprovalDecision.Approved, approval.Decision);
        Assert.Null(approval.DecisionNote);
        Assert.Equal(Now.AddMinutes(3), approval.DecidedAt);
    }

    [Fact]
    public void Apply_DoesNotMoveDecidedAtWhenOnlyDestinationChanges()
    {
        var approval = ApprovalTestData.CreateApproval(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(approval.Apply(
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            "tiktok",
            null,
            null,
            Now.AddMinutes(4)));

        Assert.Equal(2, approval.Revision);
        Assert.Equal(Now, approval.DecidedAt);
        Assert.Equal(Now.AddMinutes(4), approval.ApprovedAt);
    }

    [Fact]
    public void Apply_RejectsBlankNoteWhenRequestingChanges()
    {
        var approval = ApprovalTestData.CreateApproval(Guid.NewGuid(), Guid.NewGuid());

        var exception = Assert.Throws<ArgumentException>(() => approval.Apply(
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            "youtube",
            null,
            null,
            Now.AddMinutes(1),
            FinalApprovalDecision.ChangesRequested,
            "  "));

        Assert.Equal("decisionNote", exception.ParamName);
    }

    [Theory]
    [InlineData("approved", FinalApprovalDecision.Approved)]
    [InlineData("changes_requested", FinalApprovalDecision.ChangesRequested)]
    [InlineData("  changes_requested  ", FinalApprovalDecision.ChangesRequested)]
    public void Tokens_ParseTheCanonicalLowercaseValues(
        string token,
        FinalApprovalDecision expected)
    {
        Assert.True(FinalApprovalDecisionTokens.TryParse(token, out var decision));
        Assert.Equal(expected, decision);
        Assert.Equal(token.Trim(), FinalApprovalDecisionTokens.ToToken(decision));
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("APPROVED")]
    [InlineData("changes-requested")]
    [InlineData("rejected")]
    [InlineData("")]
    public void Tokens_RejectAnythingElse(string token)
    {
        Assert.False(FinalApprovalDecisionTokens.TryParse(token, out var decision));
        Assert.Equal(FinalApprovalDecision.Approved, decision);
    }
}
