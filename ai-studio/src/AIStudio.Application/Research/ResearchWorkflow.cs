namespace AIStudio.Application.Research;

public sealed class ResearchWorkflow(
    TrendCollectionService trendCollection,
    IResearchSynthesizer synthesizer,
    IResearchRepository repository,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<TrendSignal>> CollectAsync(
        TrendQuery query,
        CancellationToken cancellationToken) =>
        trendCollection.CollectAsync(query, cancellationToken);

    public async Task<ResearchPackage> SynthesizeAndSaveAsync(
        SynthesizeResearchInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Topic))
        {
            throw new ResearchException("research_topic_required", "Research topic is required.");
        }

        var signals = input.Signals;
        if (signals is null or { Count: 0 })
        {
            signals = await trendCollection.CollectAsync(
                new TrendQuery(
                    input.Topic,
                    input.RegionCode ?? "ID",
                    input.IncludeWatchlist,
                    input.OperatorSeedUrls),
                cancellationToken);
        }

        if (signals.Count == 0)
        {
            throw new ResearchException(
                "research_signals_empty",
                "No trend signals were collected. Provide at least one operator seed URL or enable a collector.");
        }

        var package = await synthesizer.SynthesizeAsync(
            new ResearchRequest(
                input.Topic.Trim(),
                string.IsNullOrWhiteSpace(input.RegionCode) ? "ID" : input.RegionCode.Trim().ToUpperInvariant(),
                signals,
                input.TargetAudience,
                input.OfferContext,
                input.Language ?? "Indonesian",
                input.Provider,
                input.Model),
            cancellationToken);

        var recentTitles = await repository.ListRecentProjectTitlesAsync(20, cancellationToken);
        var enriched = ApplyQualityAndNoveltyScreens(package, recentTitles, signals);

        await repository.SavePackageAsync(enriched, cancellationToken);
        return enriched;
    }

    public Task<ResearchPackage?> GetPackageAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new ResearchException("research_invalid_id", "Research package ID is required.");
        }

        return repository.FindPackageByIdAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<ResearchPackageSummary>> ListPackagesAsync(
        int take,
        CancellationToken cancellationToken) =>
        repository.ListPackagesAsync(Math.Clamp(take, 1, 50), cancellationToken);

    public Task<ProjectSourcesAndClaims?> GetProjectSourcesAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ResearchException("research_invalid_project_id", "Content project ID is required.");
        }

        return repository.GetProjectSourcesAndClaimsAsync(contentProjectId, cancellationToken);
    }

    public async Task<PromotedProjectResult?> PromoteCandidateAsync(
        Guid packageId,
        int candidateIndex,
        CancellationToken cancellationToken)
    {
        if (packageId == Guid.Empty)
        {
            throw new ResearchException("research_invalid_id", "Research package ID is required.");
        }

        var package = await repository.FindPackageByIdAsync(packageId, cancellationToken);
        if (package is null)
        {
            return null;
        }

        if (candidateIndex < 0 || candidateIndex >= package.Candidates.Count)
        {
            throw new ResearchException(
                "research_candidate_not_found",
                $"Candidate index {candidateIndex} is out of range (0..{Math.Max(package.Candidates.Count - 1, 0)}).");
        }

        var candidate = package.Candidates[candidateIndex];
        var brief = string.Join(
            "\n",
            [
                $"Hook: {candidate.Hook}",
                $"Angle ({candidate.AngleCategory} | {candidate.StrategicIntent}): {candidate.Angle}",
                $"Target Audience: {candidate.TargetAudience}",
                $"Suggested Format: {candidate.SuggestedFormat}",
                $"Why Trending: {candidate.WhyTrendingNow}"
            ]);

        if (brief.Length > 3_900)
        {
            brief = brief[..3_900];
        }

        var title = candidate.Title.Length > 200 ? candidate.Title[..200] : candidate.Title;
        var sourceIds = candidate.SupportingSourceIds.Count > 0
            ? candidate.SupportingSourceIds
            : package.Sources.Select(s => s.Id).ToArray();

        return await repository.PromoteCandidateAsync(
            packageId,
            candidateIndex,
            title,
            brief,
            sourceIds,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static ResearchPackage ApplyQualityAndNoveltyScreens(
        ResearchPackage package,
        IReadOnlyList<string> recentProjectTitles,
        IReadOnlyList<TrendSignal> signals)
    {
        var warnings = new List<FactPolicyWarning>(package.Warnings ?? []);

        var oldCount = signals.Count(s => s.FreshnessTier == FreshnessTiers.Evergreen);
        if (signals.Count > 0 && oldCount == signals.Count)
        {
            warnings.Add(new FactPolicyWarning(
                "info",
                "freshness",
                "All collected signals are >30 days old (evergreen); verify current relevance before publishing."));
        }

        var updatedCandidates = package.Candidates.Select(c =>
        {
            if (c.NoveltyScore.HasValue)
            {
                return c;
            }

            var maxOverlap = recentProjectTitles.Count == 0
                ? 0.0
                : recentProjectTitles.Max(t => ComputeWordOverlap(c.Title, t));

            return c with { NoveltyScore = Math.Round(Math.Clamp(1.0 - maxOverlap, 0.0, 1.0), 2) };
        }).ToArray();

        if (updatedCandidates.Any(c => (c.NoveltyScore ?? 1.0) < 0.35))
        {
            warnings.Add(new FactPolicyWarning(
                "warning",
                "saturation",
                "One or more candidates closely overlap existing project titles; consider picking a contrarian angle."));
        }

        return package with
        {
            Candidates = updatedCandidates,
            Warnings = warnings
        };
    }

    private static double ComputeWordOverlap(string a, string b)
    {
        var wordsA = Tokenize(a);
        var wordsB = Tokenize(b);
        if (wordsA.Count == 0 || wordsB.Count == 0)
        {
            return 0.0;
        }

        var intersection = wordsA.Intersect(wordsB, StringComparer.OrdinalIgnoreCase).Count();
        return (double)intersection / Math.Max(wordsA.Count, wordsB.Count);
    }

    private static HashSet<string> Tokenize(string value) =>
        value.Split([' ', '\t', '\n', ',', '.', ':', ';', '-', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public sealed record SynthesizeResearchInput(
    string Topic,
    string? RegionCode = "ID",
    bool IncludeWatchlist = false,
    IReadOnlyList<string>? OperatorSeedUrls = null,
    IReadOnlyList<TrendSignal>? Signals = null,
    string? TargetAudience = null,
    string? OfferContext = null,
    string? Language = "Indonesian",
    string? Provider = null,
    string? Model = null);
