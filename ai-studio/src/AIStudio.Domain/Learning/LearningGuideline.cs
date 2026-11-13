namespace AIStudio.Domain.Learning;

public static class LearningGuidelineStatuses
{
    public const string CandidateLesson = "CandidateLesson";
    public const string SupportedGuideline = "SupportedGuideline";
    public const string Retired = "Retired";
}

public static class LearningGuidelineKinds
{
    public const string ChannelGuideline = "ChannelGuideline";
    public const string AudienceInsight = "AudienceInsight";
    public const string ContentInsight = "ContentInsight";
    public const string ProductionLesson = "ProductionLesson";
}

public static class CorrectionReasonTokens
{
    public const string UnsupportedClaim = "unsupported_claim";
    public const string HookTooSlow = "hook_too_slow";
    public const string TooVerbose = "too_verbose";
    public const string ToneTooFormal = "tone_too_formal";
    public const string WeakSource = "weak_source";
    public const string VisualTooExpensive = "visual_too_expensive";
    public const string AssetRightsUnclear = "asset_rights_unclear";
    public const string StoryFlowWeak = "story_flow_weak";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UnsupportedClaim,
        HookTooSlow,
        TooVerbose,
        ToneTooFormal,
        WeakSource,
        VisualTooExpensive,
        AssetRightsUnclear,
        StoryFlowWeak
    };
}

public sealed class LearningGuideline
{
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 1_000;

    private LearningGuideline()
    {
    }

    private LearningGuideline(
        Guid id,
        string kind,
        string status,
        string title,
        string summary,
        string? sourceCorrectionReason,
        DateTimeOffset createdAt)
    {
        Id = id;
        Kind = kind;
        Status = status;
        Title = title;
        Summary = summary;
        SourceCorrectionReason = sourceCorrectionReason;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Kind { get; private set; } = LearningGuidelineKinds.ProductionLesson;

    public string Status { get; private set; } = LearningGuidelineStatuses.CandidateLesson;

    public string Title { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public string? SourceCorrectionReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? PromotedAt { get; private set; }

    public static LearningGuideline CreateCandidate(
        string kind,
        string title,
        string summary,
        string? sourceCorrectionReason,
        DateTimeOffset utcNow,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength)
        {
            throw new ArgumentException($"Title must be 1..{MaxTitleLength} chars.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(summary) || summary.Trim().Length > MaxSummaryLength)
        {
            throw new ArgumentException($"Summary must be 1..{MaxSummaryLength} chars.", nameof(summary));
        }

        return new LearningGuideline(
            id ?? Guid.NewGuid(),
            string.IsNullOrWhiteSpace(kind) ? LearningGuidelineKinds.ProductionLesson : kind.Trim(),
            LearningGuidelineStatuses.CandidateLesson,
            title.Trim(),
            summary.Trim(),
            string.IsNullOrWhiteSpace(sourceCorrectionReason) ? null : sourceCorrectionReason.Trim(),
            utcNow.ToUniversalTime());
    }

    public void PromoteToSupported(DateTimeOffset utcNow)
    {
        var now = utcNow.ToUniversalTime();
        Status = LearningGuidelineStatuses.SupportedGuideline;
        PromotedAt ??= now;
        UpdatedAt = now < UpdatedAt ? UpdatedAt : now;
    }

    public void Retire(DateTimeOffset utcNow)
    {
        var now = utcNow.ToUniversalTime();
        Status = LearningGuidelineStatuses.Retired;
        UpdatedAt = now < UpdatedAt ? UpdatedAt : now;
    }
}
