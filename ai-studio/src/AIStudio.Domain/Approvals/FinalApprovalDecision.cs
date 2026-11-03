namespace AIStudio.Domain.Approvals;

/// <summary>
/// A3 review decision. <see cref="Approved"/> keeps the original permission meaning
/// (the artifact may be published); <see cref="ChangesRequested"/> records a rejection
/// that never requires a passing QA gate, so an operator can refuse a failed QA result.
/// </summary>
public enum FinalApprovalDecision
{
    Approved = 0,
    ChangesRequested = 1
}

/// <summary>
/// Canonical lowercase decision tokens shared by HTTP and persistence. Parsing is
/// strict (case-sensitive) so an unknown token can never be silently treated as an
/// approval; only surrounding whitespace is tolerated.
/// </summary>
public static class FinalApprovalDecisionTokens
{
    public const string Approved = "approved";
    public const string ChangesRequested = "changes_requested";
    public const int MaxTokenLength = 32;

    public static string ToToken(FinalApprovalDecision decision) =>
        decision switch
        {
            FinalApprovalDecision.Approved => Approved,
            FinalApprovalDecision.ChangesRequested => ChangesRequested,
            _ => throw new ArgumentOutOfRangeException(
                nameof(decision),
                decision,
                "Unknown final approval decision.")
        };

    public static bool TryParse(string? value, out FinalApprovalDecision decision)
    {
        switch (value?.Trim())
        {
            case Approved:
                decision = FinalApprovalDecision.Approved;
                return true;
            case ChangesRequested:
                decision = FinalApprovalDecision.ChangesRequested;
                return true;
            default:
                decision = FinalApprovalDecision.Approved;
                return false;
        }
    }

    public static FinalApprovalDecision Parse(string value) =>
        TryParse(value, out var decision)
            ? decision
            : throw new InvalidOperationException(
                $"Unknown final approval decision token '{value}'.");
}
