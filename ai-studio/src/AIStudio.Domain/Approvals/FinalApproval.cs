using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;

namespace AIStudio.Domain.Approvals;

/// <summary>
/// Canonical A3 approval for one ContentProject: exactly one row per project,
/// binding the approved render artifact (relative path + SHA-256 + byte size) to the
/// final QA evidence and the publication destination. Validity is evaluated at read
/// time against current render/QA/artifact state; the row is never silently trusted.
/// </summary>
public sealed class FinalApproval
{
    public const int MaxArtifactPathLength = 1024;
    public const int MaxApproverLabelLength = 120;
    public const int MaxDecisionNoteLength = 2000;

    private FinalApproval()
    {
    }

    private FinalApproval(
        Guid id,
        Guid contentProjectId,
        Guid sourceRenderJobId,
        string artifactPath,
        string artifactContentHash,
        long artifactByteSize,
        string destination,
        string? notes,
        string? approverLabel,
        FinalApprovalDecision decision,
        string? decisionNote,
        DateTimeOffset approvedAt)
    {
        Id = id;
        ContentProjectId = contentProjectId;
        SourceRenderJobId = sourceRenderJobId;
        ArtifactPath = artifactPath;
        ArtifactContentHash = artifactContentHash;
        ArtifactByteSize = artifactByteSize;
        Destination = destination;
        Notes = notes;
        ApproverLabel = approverLabel;
        Decision = decision;
        DecisionNote = decisionNote;
        Revision = 1;
        ApprovedAt = approvedAt;
        DecidedAt = approvedAt;
        CreatedAt = approvedAt;
        UpdatedAt = approvedAt;
    }

    public Guid Id { get; private set; }

    public Guid ContentProjectId { get; private set; }

    public ContentProject ContentProject { get; private set; } = null!;

    public Guid SourceRenderJobId { get; private set; }

    public Job SourceRenderJob { get; private set; } = null!;

    public string ArtifactPath { get; private set; } = string.Empty;

    public string ArtifactContentHash { get; private set; } = string.Empty;

    public long ArtifactByteSize { get; private set; }

    public string Destination { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    public string? ApproverLabel { get; private set; }

    /// <summary>
    /// Two-way A3 review decision. Only <see cref="FinalApprovalDecision.Approved"/>
    /// may satisfy the readiness/publication gate; <see cref="FinalApprovalDecision.ChangesRequested"/>
    /// records an operator rejection.
    /// </summary>
    public FinalApprovalDecision Decision { get; private set; }

    /// <summary>Required for <see cref="FinalApprovalDecision.ChangesRequested"/>, at most 2000 characters.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>
    /// When the current decision (decision or note) was recorded. Unlike the legacy
    /// <see cref="ApprovedAt"/>, this does not move when only the destination, the
    /// approval notes, or the approver label change.
    /// </summary>
    public DateTimeOffset DecidedAt { get; private set; }

    /// <summary>
    /// Increments only when the approval binding changes (render/QA artifact or the
    /// consequential metadata destination/notes/approver/decision/decision note).
    /// Repeating the same payload is idempotent and keeps the revision.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>
    /// Legacy timestamp retained for compatibility; it tracks the last decision write.
    /// <see cref="DecidedAt"/> is authoritative for the review decision.
    /// </summary>
    public DateTimeOffset ApprovedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static FinalApproval Create(
        Guid contentProjectId,
        Guid sourceRenderJobId,
        string artifactPath,
        string artifactContentHash,
        long artifactByteSize,
        string destination,
        string? notes,
        string? approverLabel,
        DateTimeOffset utcNow,
        Guid? id = null,
        FinalApprovalDecision decision = FinalApprovalDecision.Approved,
        string? decisionNote = null)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(sourceRenderJobId, nameof(sourceRenderJobId));

        var approvalId = id ?? Guid.NewGuid();
        RequireIdentifier(approvalId, nameof(id));

        return new FinalApproval(
            approvalId,
            contentProjectId,
            sourceRenderJobId,
            RequireArtifactPath(artifactPath),
            PublicationRules.RequireHash(artifactContentHash, nameof(artifactContentHash)),
            RequireByteSize(artifactByteSize),
            PublicationRules.NormalizePlatform(destination),
            PublicationRules.OptionalNotes(notes, nameof(notes)),
            OptionalApproverLabel(approverLabel),
            decision,
            RequireDecisionNote(decision, decisionNote, nameof(decisionNote)),
            utcNow.ToUniversalTime());
    }

    /// <summary>
    /// Rebinds this project's approval. Returns <c>false</c> when the complete payload
    /// (binding, destination, notes, approver, decision, decision note) is unchanged, so
    /// an idempotent repeat does not add a revision.
    /// </summary>
    public bool Apply(
        Guid sourceRenderJobId,
        string artifactPath,
        string artifactContentHash,
        long artifactByteSize,
        string destination,
        string? notes,
        string? approverLabel,
        DateTimeOffset utcNow,
        FinalApprovalDecision decision = FinalApprovalDecision.Approved,
        string? decisionNote = null)
    {
        RequireIdentifier(sourceRenderJobId, nameof(sourceRenderJobId));

        var normalizedPath = RequireArtifactPath(artifactPath);
        var normalizedHash = PublicationRules.RequireHash(
            artifactContentHash,
            nameof(artifactContentHash));
        var normalizedSize = RequireByteSize(artifactByteSize);
        var normalizedDestination = PublicationRules.NormalizePlatform(destination);
        var normalizedNotes = PublicationRules.OptionalNotes(notes, nameof(notes));
        var normalizedApprover = OptionalApproverLabel(approverLabel);
        var normalizedDecisionNote = RequireDecisionNote(
            decision,
            decisionNote,
            nameof(decisionNote));
        var decisionChanged = Decision != decision
            || !string.Equals(DecisionNote, normalizedDecisionNote, StringComparison.Ordinal);

        if (SourceRenderJobId == sourceRenderJobId
            && string.Equals(ArtifactPath, normalizedPath, StringComparison.Ordinal)
            && string.Equals(ArtifactContentHash, normalizedHash, StringComparison.Ordinal)
            && ArtifactByteSize == normalizedSize
            && string.Equals(Destination, normalizedDestination, StringComparison.Ordinal)
            && string.Equals(Notes, normalizedNotes, StringComparison.Ordinal)
            && string.Equals(ApproverLabel, normalizedApprover, StringComparison.Ordinal)
            && !decisionChanged)
        {
            return false;
        }

        SourceRenderJobId = sourceRenderJobId;
        ArtifactPath = normalizedPath;
        ArtifactContentHash = normalizedHash;
        ArtifactByteSize = normalizedSize;
        Destination = normalizedDestination;
        Notes = normalizedNotes;
        ApproverLabel = normalizedApprover;
        Decision = decision;
        DecisionNote = normalizedDecisionNote;
        Revision++;
        var updatedAt = NormalizeUpdateTime(utcNow);
        if (decisionChanged)
        {
            DecidedAt = updatedAt;
        }

        ApprovedAt = updatedAt;
        UpdatedAt = updatedAt;
        return true;
    }

    private DateTimeOffset NormalizeUpdateTime(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        return utcValue < UpdatedAt
            ? throw new ArgumentOutOfRangeException(
                nameof(value),
                "Update time cannot precede the previous update.")
            : utcValue;
    }

    private static string RequireArtifactPath(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxArtifactPathLength)
        {
            throw new ArgumentException(
                $"Artifact path must contain 1 to {MaxArtifactPathLength} characters.",
                nameof(value));
        }

        if (normalized.StartsWith('/') || normalized.Contains('\\'))
        {
            throw new ArgumentException(
                "Artifact path must be a relative, forward-slash path.",
                nameof(value));
        }

        if (normalized.Split('/').Any(segment => segment is "." or ".." or ""))
        {
            throw new ArgumentException(
                "Artifact path cannot contain empty, current, or parent segments.",
                nameof(value));
        }

        return normalized;
    }

    private static long RequireByteSize(long value) =>
        value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                "Artifact byte size must be greater than zero.");

    private static string? OptionalApproverLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length > MaxApproverLabelLength
            ? throw new ArgumentException(
                $"Approver label must contain at most {MaxApproverLabelLength} characters.",
                nameof(value))
            : normalized;
    }

    /// <summary>
    /// Applies the canonical decision-note rule: optional for an approval, mandatory and
    /// at most <see cref="MaxDecisionNoteLength"/> characters for a change request.
    /// Exposed so other layers (for example project transfer) validate against the same
    /// rule instead of duplicating it.
    /// </summary>
    public static string? NormalizeDecisionNote(
        FinalApprovalDecision decision,
        string? decisionNote) =>
        RequireDecisionNote(decision, decisionNote, nameof(decisionNote));

    /// <summary>
    /// A change request is only actionable when it says what must change, so the note is
    /// mandatory for that decision. An approval keeps the note optional.
    /// </summary>
    private static string? RequireDecisionNote(
        FinalApprovalDecision decision,
        string? value,
        string parameterName)
    {
        _ = FinalApprovalDecisionTokens.ToToken(decision);

        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (decision == FinalApprovalDecision.ChangesRequested && normalized is null)
        {
            throw new ArgumentException(
                "A decision note is required when requesting changes.",
                parameterName);
        }

        return normalized is { Length: > MaxDecisionNoteLength }
            ? throw new ArgumentException(
                $"Decision note must contain at most {MaxDecisionNoteLength} characters.",
                parameterName)
            : normalized;
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }
}
