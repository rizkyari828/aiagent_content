namespace AIStudio.Application.Research;

public static class ResearchSourceTypes
{
    public const string YouTube = "youtube";
    public const string GoogleTrends = "google_trends";
    public const string WatchlistRss = "watchlist_rss";
    public const string WebSearch = "web_search";
    public const string OperatorSeed = "operator_seed";
}

public static class ClaimKinds
{
    public const string VerifiedStat = "verified_stat";
    public const string ObservedPattern = "observed_pattern";
    public const string SynthesisHypothesis = "synthesis_hypothesis";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        VerifiedStat,
        ObservedPattern,
        SynthesisHypothesis
    };
}

public static class FreshnessTiers
{
    public const string Breaking = "breaking";
    public const string Current = "current";
    public const string Evergreen = "evergreen";
}

public static class ContentAngles
{
    public const string MythBusting = "myth_busting";
    public const string RealityCostCheck = "reality_cost_check";
    public const string BeginnerMistakes = "beginner_mistakes";
    public const string StepByStepPlaybook = "step_by_step_playbook";
    public const string ComparisonBenchmark = "comparison_benchmark";
    public const string CaseStudy = "case_study";

    public static readonly IReadOnlyList<string> All =
    [
        MythBusting,
        RealityCostCheck,
        BeginnerMistakes,
        StepByStepPlaybook,
        ComparisonBenchmark,
        CaseStudy
    ];
}

public static class CandidateIntents
{
    public const string TrafficMagnet = "traffic_magnet";
    public const string OfferBridge = "offer_bridge";
}

public sealed record TrendMetrics(
    long? ViewCount = null,
    long? LikeCount = null,
    long? CommentCount = null,
    double? ViewsPerHour = null,
    double? OutlierRatio = null,
    double? EngagementRate = null);

public sealed record CommentInsight(
    string Text,
    int LikeCount = 0,
    string? Category = null);

public sealed record TrendSignal(
    string SourceType,
    string ExternalId,
    string Title,
    string Url,
    string? ChannelOrAuthor,
    DateTimeOffset? PublishedAt,
    DateTimeOffset RetrievedAt,
    TrendMetrics Metrics,
    string RawSummary,
    string FreshnessTier = FreshnessTiers.Current,
    string ContentHash = "",
    IReadOnlyList<CommentInsight>? TopComments = null);

public sealed record ResearchSource(
    Guid Id,
    Guid? ContentProjectId,
    string Title,
    string Url,
    string SourceType,
    DateTimeOffset RetrievedAt,
    string SnapshotHash);

public sealed record EvidenceClaim(
    Guid Id,
    Guid ResearchPackageId,
    Guid? SourceId,
    string ClaimText,
    string ClaimKind,
    string? ConfidenceNote = null,
    string? VisualProofSuggestion = null);

public sealed record IdeaCandidate(
    string Title,
    string Hook,
    string TargetAudience,
    string Angle,
    string SuggestedFormat,
    string WhyTrendingNow,
    IReadOnlyList<Guid> SupportingSourceIds,
    string RecommendedVisualStyle,
    string AngleCategory = ContentAngles.StepByStepPlaybook,
    string StrategicIntent = CandidateIntents.TrafficMagnet,
    double? NoveltyScore = null,
    IReadOnlyList<string>? AtomizedShorts = null,
    IReadOnlyList<string>? TitleVariants = null);

public sealed record PacingBlueprint(
    string HookPattern,
    string PatternInterrupts,
    string ProofOrDemoStyle,
    string CtaStyle);

public sealed record FactPolicyWarning(
    string Severity,
    string Category,
    string Message);

public sealed record ResearchPackage(
    Guid Id,
    string QueryTopic,
    string RegionCode,
    DateTimeOffset CreatedAt,
    string ProviderUsed,
    IReadOnlyList<ResearchSource> Sources,
    IReadOnlyList<EvidenceClaim> Claims,
    IReadOnlyList<IdeaCandidate> Candidates,
    PacingBlueprint? PacingBlueprint = null,
    IReadOnlyList<FactPolicyWarning>? Warnings = null,
    bool DegradedFallback = false);

public sealed record TrendQuery(
    string Topic,
    string RegionCode = "ID",
    bool IncludeWatchlist = false,
    IReadOnlyList<string>? OperatorSeedUrls = null,
    int MaxSignals = 15);

public sealed record ResearchRequest(
    string Topic,
    string RegionCode = "ID",
    IReadOnlyList<TrendSignal>? Signals = null,
    string? TargetAudience = null,
    string? OfferContext = null,
    string? Language = "Indonesian",
    string? Provider = null,
    string? Model = null);

public sealed record WatchlistFeedDescriptor(
    string Id,
    string Title,
    string Url,
    string Category);
