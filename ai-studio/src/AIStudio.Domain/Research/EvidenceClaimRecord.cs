namespace AIStudio.Domain.Research;

public sealed class EvidenceClaimRecord
{
    public const int MaxClaimLength = 2_000;
    public const int MaxKindLength = 64;
    public const int MaxNoteLength = 1_000;
    public const int MaxVisualProofLength = 128;

    private EvidenceClaimRecord()
    {
    }

    private EvidenceClaimRecord(
        Guid id,
        Guid researchPackageId,
        Guid? sourceId,
        Guid? contentProjectId,
        string claimText,
        string claimKind,
        string? confidenceNote,
        string? visualProofSuggestion)
    {
        Id = id;
        ResearchPackageId = researchPackageId;
        SourceId = sourceId;
        ContentProjectId = contentProjectId;
        ClaimText = claimText;
        ClaimKind = claimKind;
        ConfidenceNote = confidenceNote;
        VisualProofSuggestion = visualProofSuggestion;
    }

    public Guid Id { get; private set; }

    public Guid ResearchPackageId { get; private set; }

    public Guid? SourceId { get; private set; }

    public Guid? ContentProjectId { get; private set; }

    public string ClaimText { get; private set; } = string.Empty;

    public string ClaimKind { get; private set; } = string.Empty;

    public string? ConfidenceNote { get; private set; }

    public string? VisualProofSuggestion { get; private set; }

    public static EvidenceClaimRecord Create(
        Guid id,
        Guid researchPackageId,
        Guid? sourceId,
        Guid? contentProjectId,
        string claimText,
        string claimKind,
        string? confidenceNote,
        string? visualProofSuggestion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Claim ID cannot be empty.", nameof(id));
        }

        if (researchPackageId == Guid.Empty)
        {
            throw new ArgumentException("Research package ID cannot be empty.", nameof(researchPackageId));
        }

        var normalizedClaim = claimText.Trim();
        if (normalizedClaim.Length > MaxClaimLength)
        {
            normalizedClaim = normalizedClaim[..MaxClaimLength];
        }

        var normalizedNote = string.IsNullOrWhiteSpace(confidenceNote)
            ? null
            : (confidenceNote.Trim().Length > MaxNoteLength ? confidenceNote.Trim()[..MaxNoteLength] : confidenceNote.Trim());

        return new EvidenceClaimRecord(
            id,
            researchPackageId,
            sourceId,
            contentProjectId,
            normalizedClaim,
            claimKind.Trim(),
            normalizedNote,
            string.IsNullOrWhiteSpace(visualProofSuggestion) ? null : visualProofSuggestion.Trim());
    }

    public void AttachToProject(Guid contentProjectId)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException("Content project ID cannot be empty.", nameof(contentProjectId));
        }

        ContentProjectId = contentProjectId;
    }
}
