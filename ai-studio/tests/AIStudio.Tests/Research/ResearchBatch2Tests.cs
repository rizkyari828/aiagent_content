using AIStudio.Application.Learning;
using AIStudio.Application.Research;
using AIStudio.Domain.Learning;
using AIStudio.Infrastructure.Research;
using Xunit;

namespace AIStudio.Tests.Research;

public sealed class ResearchBatch2Tests
{
    [Fact]
    public async Task ResearchWorkflow_SynthesizeAndSave_ComputesNoveltyAndSaturationWarning()
    {
        var repo = new InMemoryResearchRepository(["Cara Hemat VRAM 16GB Untuk Ollama"]);
        var synth = new StaticSynthesizer(new ResearchPackage(
            Guid.NewGuid(),
            "ollama vram",
            "ID",
            DateTimeOffset.UtcNow,
            "ollama:test",
            [new ResearchSource(Guid.NewGuid(), null, "S1", "https://example.com/1", ResearchSourceTypes.OperatorSeed, DateTimeOffset.UtcNow, "h1")],
            [],
            [
                new IdeaCandidate(
                    "Cara Hemat VRAM 16GB Untuk Ollama",
                    "Stop OOM today",
                    "Devs",
                    "Practical",
                    "long",
                    "Trending",
                    [],
                    "explainer")
            ]));

        var workflow = new ResearchWorkflow(
            new TrendCollectionService([], new InMemoryTrendCacheStore(), TimeProvider.System),
            synth,
            repo,
            TimeProvider.System);

        var signal = new TrendSignal(
            ResearchSourceTypes.OperatorSeed,
            "s1",
            "Seed",
            "https://example.com/1",
            "op",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new TrendMetrics(),
            "Summary");

        var saved = await workflow.SynthesizeAndSaveAsync(
            new SynthesizeResearchInput("ollama vram", Signals: [signal]),
            TestContext.Current.CancellationToken);

        Assert.NotNull(saved.Candidates[0].NoveltyScore);
        Assert.True(saved.Candidates[0].NoveltyScore < 0.35);
        Assert.Contains(saved.Warnings ?? [], w => w.Category == "saturation");
    }

    [Fact]
    public async Task ResearchWorkflow_PromoteCandidate_CreatesProjectAndLinksSources()
    {
        var sourceId = Guid.NewGuid();
        var pkgId = Guid.NewGuid();
        var repo = new InMemoryResearchRepository([]);
        await repo.SavePackageAsync(new ResearchPackage(
            pkgId,
            "ai local",
            "ID",
            DateTimeOffset.UtcNow,
            "test",
            [new ResearchSource(sourceId, null, "Source 1", "https://example.com/1", ResearchSourceTypes.YouTube, DateTimeOffset.UtcNow, "h1")],
            [],
            [
                new IdeaCandidate(
                    "Local AI Guide",
                    "Hook",
                    "Devs",
                    "Angle",
                    "short",
                    "Why",
                    [sourceId],
                    "explainer")
            ]),
            TestContext.Current.CancellationToken);

        var workflow = new ResearchWorkflow(
            new TrendCollectionService([], new InMemoryTrendCacheStore(), TimeProvider.System),
            new StaticSynthesizer(null!),
            repo,
            TimeProvider.System);

        var promoted = await workflow.PromoteCandidateAsync(pkgId, 0, TestContext.Current.CancellationToken);

        Assert.NotNull(promoted);
        Assert.Equal("Local AI Guide", promoted.Title);
        Assert.Equal(1, promoted.LinkedSourceCount);
    }

    [Fact]
    public async Task LearningWorkflow_RequiresExplicitPromoteBeforeAppearingInActiveGuidelines()
    {
        var repo = new InMemoryLearningRepository();
        var workflow = new LearningWorkflow(repo, TimeProvider.System);

        var candidate = await workflow.CreateCandidateAsync(
            LearningGuidelineKinds.ProductionLesson,
            "Hook max 18 words",
            "Keep openingHook sharp under 18 words.",
            CorrectionReasonTokens.HookTooSlow,
            TestContext.Current.CancellationToken);

        Assert.Equal(LearningGuidelineStatuses.CandidateLesson, candidate.Status);
        Assert.Empty(await workflow.GetActiveGuidelineSummariesAsync(5, TestContext.Current.CancellationToken));

        var promoted = await workflow.PromoteAsync(candidate.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(promoted);
        Assert.Equal(LearningGuidelineStatuses.SupportedGuideline, promoted.Status);

        var active = await workflow.GetActiveGuidelineSummariesAsync(5, TestContext.Current.CancellationToken);
        Assert.Single(active);
        Assert.Contains("Hook max 18 words", active[0]);
    }

    private sealed class StaticSynthesizer(ResearchPackage package) : IResearchSynthesizer
    {
        public Task<ResearchPackage> SynthesizeAsync(ResearchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(package);
    }

    private sealed class InMemoryResearchRepository(IReadOnlyList<string> recentTitles) : IResearchRepository
    {
        private readonly Dictionary<Guid, ResearchPackage> store = [];

        public Task SavePackageAsync(ResearchPackage package, CancellationToken cancellationToken)
        {
            store[package.Id] = package;
            return Task.CompletedTask;
        }

        public Task<ResearchPackage?> FindPackageByIdAsync(Guid packageId, CancellationToken cancellationToken) =>
            Task.FromResult(store.GetValueOrDefault(packageId));

        public Task<IReadOnlyList<ResearchPackageSummary>> ListPackagesAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ResearchPackageSummary>>([]);

        public Task<ProjectSourcesAndClaims?> GetProjectSourcesAndClaimsAsync(Guid contentProjectId, CancellationToken cancellationToken) =>
            Task.FromResult<ProjectSourcesAndClaims?>(null);

        public Task<PromotedProjectResult?> PromoteCandidateAsync(
            Guid packageId,
            int candidateIndex,
            string projectTitle,
            string projectBrief,
            IReadOnlyList<Guid> sourceIds,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            Task.FromResult<PromotedProjectResult?>(
                new PromotedProjectResult(Guid.NewGuid(), projectTitle, packageId, candidateIndex, sourceIds.Count));

        public Task<IReadOnlyList<string>> ListRecentProjectTitlesAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult(recentTitles);
    }

    private sealed class InMemoryLearningRepository : ILearningRepository
    {
        private readonly List<LearningGuideline> items = [];

        public Task<IReadOnlyList<LearningGuideline>> ListAsync(string? status, int take, CancellationToken cancellationToken)
        {
            var query = string.IsNullOrWhiteSpace(status)
                ? items
                : items.Where(x => x.Status == status);
            return Task.FromResult<IReadOnlyList<LearningGuideline>>(query.Take(take).ToArray());
        }

        public Task<LearningGuideline?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(items.FirstOrDefault(x => x.Id == id));

        public void Add(LearningGuideline guideline) => items.Add(guideline);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
