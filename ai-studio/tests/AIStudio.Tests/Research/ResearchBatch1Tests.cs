using System.Net;
using System.Text;
using AIStudio.Application.AI;
using AIStudio.Application.Research;
using AIStudio.Infrastructure.Research;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Research;

public sealed class ResearchBatch1Tests
{
    [Fact]
    public void UrlCanonicalizer_StripsTrackingParamsAndRejectsNonHttp()
    {
        var cleaned = UrlCanonicalizer.TryCanonicalize(
            "HTTPS://YouTube.com/watch?v=abc123&utm_source=twitter&si=xyz&fbclid=123#frag");

        Assert.Equal("https://youtube.com/watch?v=abc123", cleaned);
        Assert.Null(UrlCanonicalizer.TryCanonicalize("file:///etc/passwd"));
        Assert.Null(UrlCanonicalizer.TryCanonicalize("javascript:alert(1)"));
        Assert.Null(UrlCanonicalizer.TryCanonicalize("not-a-url"));
    }

    [Fact]
    public void TrendMetricsCalculator_ComputesOutlierVelocityAndEngagementDeterministically()
    {
        var retrieved = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        var published = retrieved.AddHours(-10);

        var metrics = TrendMetricsCalculator.Compute(
            viewCount: 50_000,
            likeCount: 2_500,
            commentCount: 500,
            publishedAt: published,
            retrievedAt: retrieved,
            channelMedianRecentViews: 5_000);

        Assert.Equal(5_000.0, metrics.ViewsPerHour);
        Assert.Equal(10.0, metrics.OutlierRatio);
        Assert.Equal(0.06, metrics.EngagementRate);
    }

    [Fact]
    public void CitationPolicy_DowngradesVerifiedStatWithoutValidSourceUrl()
    {
        var validSource = new ResearchSource(
            Guid.NewGuid(),
            null,
            "YouTube Video",
            "https://www.youtube.com/watch?v=xyz",
            ResearchSourceTypes.YouTube,
            DateTimeOffset.UtcNow,
            "hash1");

        var claims = new[]
        {
            new EvidenceClaim(Guid.NewGuid(), Guid.NewGuid(), validSource.Id, "50k views in 10h", ClaimKinds.VerifiedStat),
            new EvidenceClaim(Guid.NewGuid(), Guid.NewGuid(), null, "90% people fail", ClaimKinds.VerifiedStat),
            new EvidenceClaim(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Unlinked source", ClaimKinds.VerifiedStat)
        };

        var normalized = CitationPolicy.NormalizeClaims(
            claims,
            new Dictionary<Guid, ResearchSource> { [validSource.Id] = validSource });

        Assert.Equal(ClaimKinds.VerifiedStat, normalized[0].ClaimKind);
        Assert.Equal(ClaimKinds.SynthesisHypothesis, normalized[1].ClaimKind);
        Assert.Null(normalized[1].SourceId);
        Assert.Equal(ClaimKinds.SynthesisHypothesis, normalized[2].ClaimKind);
    }

    [Fact]
    public void ResearchTokenDiet_DeduplicatesByUrlAndHashAndOrdersByOutlierRatio()
    {
        var now = DateTimeOffset.UtcNow;
        var s1 = new TrendSignal(
            ResearchSourceTypes.YouTube,
            "v1",
            "Low outlier",
            "https://youtube.com/watch?v=1&utm_source=x",
            "Ch1",
            now.AddHours(-5),
            now,
            new TrendMetrics(1_000, 10, 1, 200, 1.0, 0.011),
            new string('a', 1500));
        var s2 = new TrendSignal(
            ResearchSourceTypes.YouTube,
            "v1-dup",
            "Low outlier dup",
            "https://youtube.com/watch?v=1",
            "Ch1",
            now.AddHours(-5),
            now,
            new TrendMetrics(1_000, 10, 1, 200, 1.0, 0.011),
            new string('a', 1500));
        var s3 = new TrendSignal(
            ResearchSourceTypes.YouTube,
            "v2",
            "High outlier",
            "https://youtube.com/watch?v=2",
            "Ch2",
            now.AddHours(-2),
            now,
            new TrendMetrics(80_000, 4000, 200, 40_000, 16.0, 0.0525),
            "Summary 2");

        var trimmed = ResearchTokenDiet.TrimAndDeduplicate([s1, s2, s3], maxSignals: 10, maxSummaryChars: 100);

        Assert.Equal(2, trimmed.Count);
        Assert.Equal("v2", trimmed[0].ExternalId);
        Assert.Equal("v1", trimmed[1].ExternalId);
        Assert.True(trimmed[1].RawSummary.Length <= 103);
    }

    [Fact]
    public async Task YouTubeDataApiTrendCollector_ParsesVideosAndMinesComments_WithoutExposingKey()
    {
        var handler = new StubHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/search?"))
            {
                return JsonResponse("""{"items":[{"id":{"videoId":"vid1"}}]}""");
            }

            if (url.Contains("/videos?"))
            {
                return JsonResponse("""
                {
                  "items": [{
                    "id": "vid1",
                    "snippet": {
                      "title": "Local AI 16GB Benchmark",
                      "channelTitle": "IndoTech",
                      "description": "Real test on RTX 4060 Ti",
                      "publishedAt": "2026-11-13T00:00:00Z"
                    },
                    "statistics": {
                      "viewCount": "25000",
                      "likeCount": "1200",
                      "commentCount": "140"
                    }
                  }]
                }
                """);
            }

            if (url.Contains("/commentThreads?"))
            {
                return JsonResponse("""
                {
                  "items": [{
                    "snippet": {
                      "topLevelComment": {
                        "snippet": {
                          "textDisplay": "Gimana cara setting VRAM 16GB nya bang?",
                          "likeCount": 42
                        }
                      }
                    }
                  }]
                }
                """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(handler);
        var collector = new YouTubeDataApiTrendCollector(
            client,
            Options.Create(new YouTubeOptions { Enabled = true, ApiKey = "secret-test-key" }),
            TimeProvider.System);

        var signals = await collector.CollectAsync(
            new TrendQuery("local ai", "ID"),
            TestContext.Current.CancellationToken);

        Assert.Single(signals);
        Assert.Equal("Local AI 16GB Benchmark", signals[0].Title);
        Assert.Equal(25_000, signals[0].Metrics.ViewCount);
        Assert.Single(signals[0].TopComments!);
        Assert.Contains("VRAM 16GB", signals[0].TopComments![0].Text);
    }

    [Fact]
    public async Task GoogleTrendsRssCollector_ParsesValidRssItems()
    {
        const string rss = """
            <?xml version="1.0" encoding="UTF-8" ?>
            <rss version="2.0">
              <channel>
                <title>Daily Search Trends</title>
                <item>
                  <title>Qwen 3.6 Coding</title>
                  <link>https://trends.google.com/trends/explore?q=qwen&amp;utm_source=rss</link>
                  <description>&lt;b&gt;AI&lt;/b&gt; local coding model</description>
                  <pubDate>Fri, 13 Nov 2026 10:00:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var client = new HttpClient(new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(rss, Encoding.UTF8, "application/xml")
            }));

        var collector = new GoogleTrendsRssCollector(
            client,
            Options.Create(new ResearchOptions { GoogleTrendsEnabled = true }),
            TimeProvider.System);

        var signals = await collector.CollectAsync(
            new TrendQuery("ai", "ID"),
            TestContext.Current.CancellationToken);

        Assert.Single(signals);
        Assert.Equal("Qwen 3.6 Coding", signals[0].Title);
        Assert.Equal("https://trends.google.com/trends/explore?q=qwen", signals[0].Url);
        Assert.DoesNotContain("<b>", signals[0].RawSummary);
    }

    [Fact]
    public async Task TrendCollectionService_CachesRepeatedQueryWithinTtl()
    {
        var calls = 0;
        var collector = new CountingCollector(() =>
        {
            calls++;
            return
            [
                new TrendSignal(
                    ResearchSourceTypes.OperatorSeed,
                    "s1",
                    "FYP Seed",
                    "https://tiktok.com/@user/video/123",
                    "op",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    new TrendMetrics(),
                    "Seed")
            ];
        });

        var service = new TrendCollectionService(
            [collector],
            new InMemoryTrendCacheStore(),
            TimeProvider.System);

        var first = await service.CollectAsync(new TrendQuery("ai", "ID"), TestContext.Current.CancellationToken);
        var second = await service.CollectAsync(new TrendQuery("ai", "ID"), TestContext.Current.CancellationToken);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ResearchSynthesizer_FallsBackGracefullyWhenAiFails()
    {
        var synth = new ResearchSynthesizer(
            new FailingAiGenerator(AiErrorCode.ProviderUnavailable),
            TimeProvider.System);

        var signal = new TrendSignal(
            ResearchSourceTypes.YouTube,
            "v1",
            "Local AI Guide",
            "https://youtube.com/watch?v=abc",
            "Channel",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new TrendMetrics(10_000),
            "Summary");

        var package = await synth.SynthesizeAsync(
            new ResearchRequest("local ai", "ID", [signal]),
            TestContext.Current.CancellationToken);

        Assert.True(package.DegradedFallback);
        Assert.Equal("deterministic-fallback", package.ProviderUsed);
        Assert.NotEmpty(package.Sources);
        Assert.NotEmpty(package.Candidates);
    }

    [Fact]
    public async Task ResearchSynthesizer_ParsesValidJsonAndNormalizesClaims()
    {
        const string json = """
            ```json
            {
              "claims": [
                { "sourceIndex": 0, "claimText": "Video gained 10,000 views", "claimKind": "verified_stat", "visualProofSuggestion": "visual.diagram" },
                { "sourceIndex": null, "claimText": "Audience prefers short demos", "claimKind": "verified_stat" }
              ],
              "candidates": [
                {
                  "title": "Cara Hemat VRAM",
                  "hook": "Jangan beli GPU baru dulu sebelum cek ini.",
                  "targetAudience": "Dev lokal",
                  "angle": "Benchmark nyata 16GB",
                  "angleCategory": "reality_cost_check",
                  "strategicIntent": "offer_bridge",
                  "suggestedFormat": "long-explainer",
                  "whyTrendingNow": "Banyak pertanyaan VRAM di komentar",
                  "supportingSourceIndices": [0],
                  "recommendedVisualStyle": "explainer",
                  "noveltyScore": 0.85,
                  "atomizedShorts": ["Clip 1: VRAM Check"],
                  "titleVariants": ["Hemat VRAM 16GB", "Setting Ollama Anti OOM"]
                }
              ],
              "pacingBlueprint": {
                "hookPattern": "Contrarian opening",
                "patternInterrupts": "Every 20s benchmark chart",
                "proofOrDemoStyle": "Live terminal run",
                "ctaStyle": "Checklist download"
              }
            }
            ```
            """;

        var synth = new ResearchSynthesizer(
            new StaticAiGenerator(json),
            TimeProvider.System);

        var signal = new TrendSignal(
            ResearchSourceTypes.YouTube,
            "v1",
            "Local AI Guide",
            "https://youtube.com/watch?v=abc",
            "Channel",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new TrendMetrics(10_000),
            "Summary");

        var package = await synth.SynthesizeAsync(
            new ResearchRequest("local ai", "ID", [signal]),
            TestContext.Current.CancellationToken);

        Assert.False(package.DegradedFallback);
        Assert.Equal(2, package.Claims.Count);
        Assert.Equal(ClaimKinds.VerifiedStat, package.Claims[0].ClaimKind);
        Assert.Equal(ClaimKinds.SynthesisHypothesis, package.Claims[1].ClaimKind);
        Assert.Single(package.Candidates);
        Assert.Equal(ContentAngles.RealityCostCheck, package.Candidates[0].AngleCategory);
        Assert.Equal(CandidateIntents.OfferBridge, package.Candidates[0].StrategicIntent);
        Assert.NotNull(package.PacingBlueprint);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class CountingCollector(Func<IReadOnlyList<TrendSignal>> factory) : ITrendCollector
    {
        public string SourceType => ResearchSourceTypes.OperatorSeed;
        public bool IsEnabled => true;

        public Task<IReadOnlyList<TrendSignal>> CollectAsync(TrendQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(factory());
    }

    private sealed class FailingAiGenerator(AiErrorCode code) : IAiTextGenerator
    {
        public Task<AiTextResponse> GenerateAsync(AiTextRequest request, CancellationToken cancellationToken) =>
            throw new AiGenerationException(code, "Simulated failure");
    }

    private sealed class StaticAiGenerator(string responseText) : IAiTextGenerator
    {
        public Task<AiTextResponse> GenerateAsync(AiTextRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AiTextResponse(responseText, "test-model", null, null, null));
    }
}
