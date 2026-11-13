using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Learning;
using AIStudio.Application.Quality;
using AIStudio.Application.Research;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Learning;
using Xunit;

namespace AIStudio.Tests.Quality;

public sealed class PreRenderQualityTests
{
    [Fact]
    public void Inspect_FlagsSpokenLeakAndHookLengthAndPacingDeviation()
    {
        var script = new GenerateScriptResultStub(
            "Title",
            "Halo semuanya selamat datang kembali di channel ini hari ini kita akan membahas topik yang sangat menarik sekali buat kalian semua",
            [new("Intro", "Cek link ini https://example.com [SFX] sekarang.")],
            "Terima kasih.").ToModel();

        var report = PreRenderQualityChecker.Inspect(script, srtContent: null, targetDurationSeconds: 60);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, i => i.Category == "spoken_leak" && i.Severity == "error");
        Assert.Contains(report.Issues, i => i.Category == "hook_length");
        Assert.Contains(report.Issues, i => i.Category == "pacing_deviation");
    }

    [Fact]
    public void InspectSrt_DetectsCpsAndLineLengthAndLineCount()
    {
        const string srt = """
            1
            00:00:00,000 --> 00:00:01,000
            Ini adalah baris teks subtitle yang sangat panjang sekali lebih dari empat puluh dua karakter
            Baris kedua
            Baris ketiga
            """;

        var issues = PreRenderQualityChecker.InspectSrt(srt, out var cueCount);

        Assert.Equal(1, cueCount);
        Assert.Contains(issues, i => i.Category == "srt_line_count");
        Assert.Contains(issues, i => i.Category == "srt_line_length");
        Assert.Contains(issues, i => i.Category == "srt_cps");
    }

    [Fact]
    public void GenerateIdeaResult_PreservesBackwardCompatibilityWithAndWithoutVariants()
    {
        const string legacyJson = """
            {"title":"T","hook":"H","summary":"S","angle":"A","targetAudience":"TA","suggestedFormat":"F"}
            """;

        var parsed = GenerateIdeaResult.Deserialize(legacyJson);
        Assert.Null(parsed.Variants);
        Assert.DoesNotContain("variants", parsed.Serialize());

        var withVariants = parsed with
        {
            Variants = [new GenerateIdeaVariant("T2", "H2", "S2", "A2", "TA2", "F2")]
        };
        Assert.Contains("variants", withVariants.Serialize());
    }

    [Fact]
    public async Task GenerateIdeaJobHandler_InjectsNegativeHistoryAndActiveGuidelines()
    {
        var projectId = Guid.NewGuid();
        var capturedPrompt = string.Empty;
        var ai = new CapturingAiGenerator(p =>
        {
            capturedPrompt = p;
            return """{"title":"T","hook":"H","summary":"S","angle":"A","targetAudience":"TA","suggestedFormat":"F"}""";
        });

        var handler = new GenerateIdeaJobHandler(
            new SingleProjectReader(new ContentProjectSnapshot(projectId, "Local AI", "Brief")),
            ai,
            new StubResearchRepo(["Old Video 1"]),
            new StubLearningRepo(["Keep hook punchy"]));

        var job = new ClaimedJob(
            Guid.NewGuid(),
            projectId,
            JobType.GenerateIdea,
            "v1",
            JsonSerializer.Serialize(new GenerateIdeaJobPayload(projectId, "Ollama", "Devs", "Indonesian")),
            0,
            1,
            false);

        await handler.ExecuteAsync(job, TestContext.Current.CancellationToken);

        Assert.Contains("Old Video 1", capturedPrompt);
        Assert.Contains("Keep hook punchy", capturedPrompt);
    }

    private sealed record GenerateScriptResultStub(
        string Title,
        string OpeningHook,
        IReadOnlyList<SectionStub> Sections,
        string Closing)
    {
        public AIStudio.Application.Jobs.GenerateScript.GenerateScriptResult ToModel() =>
            new(Title, OpeningHook, Sections.Select(s => new AIStudio.Application.Jobs.GenerateScript.GenerateScriptSection(s.Heading, s.Narration)).ToArray(), Closing);
    }

    private sealed record SectionStub(string Heading, string Narration);

    private sealed class CapturingAiGenerator(Func<string, string> responder) : IAiTextGenerator
    {
        public Task<AiTextResponse> GenerateAsync(AiTextRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AiTextResponse(responder(request.Prompt), "test", null, null, null));
    }

    private sealed class SingleProjectReader(ContentProjectSnapshot project) : IContentProjectReader
    {
        public Task<ContentProjectSnapshot?> FindByIdAsync(Guid contentProjectId, CancellationToken cancellationToken) =>
            Task.FromResult<ContentProjectSnapshot?>(project.Id == contentProjectId ? project : null);
    }

    private sealed class StubResearchRepo(IReadOnlyList<string> titles) : IResearchRepository
    {
        public Task SavePackageAsync(ResearchPackage package, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ResearchPackage?> FindPackageByIdAsync(Guid packageId, CancellationToken cancellationToken) => Task.FromResult<ResearchPackage?>(null);
        public Task<IReadOnlyList<ResearchPackageSummary>> ListPackagesAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ResearchPackageSummary>>([]);
        public Task<ProjectSourcesAndClaims?> GetProjectSourcesAndClaimsAsync(Guid contentProjectId, CancellationToken cancellationToken) => Task.FromResult<ProjectSourcesAndClaims?>(null);
        public Task<PromotedProjectResult?> PromoteCandidateAsync(Guid packageId, int candidateIndex, string projectTitle, string projectBrief, IReadOnlyList<Guid> sourceIds, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.FromResult<PromotedProjectResult?>(null);
        public Task<IReadOnlyList<string>> ListRecentProjectTitlesAsync(int take, CancellationToken cancellationToken) => Task.FromResult(titles);
    }

    private sealed class StubLearningRepo(IReadOnlyList<string> titles) : ILearningRepository
    {
        public Task<IReadOnlyList<LearningGuideline>> ListAsync(string? status, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LearningGuideline>>(
                titles.Select(t => LearningGuideline.CreateCandidate(LearningGuidelineKinds.ChannelGuideline, t, t, null, DateTimeOffset.UtcNow)).ToArray());

        public Task<LearningGuideline?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<LearningGuideline?>(null);
        public void Add(LearningGuideline guideline) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
