using System.Text.Json;

namespace AIStudio.Application.Research;

public static class ResearchPackageParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static ResearchPackage Parse(
        Guid packageId,
        string queryTopic,
        string regionCode,
        DateTimeOffset createdAt,
        string providerUsed,
        IReadOnlyList<ResearchSource> sources,
        string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new ResearchException("research_malformed_response", "Research synthesizer returned an empty response.");
        }

        var cleaned = StripMarkdownFence(rawJson);
        RawSynthesisDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RawSynthesisDto>(cleaned, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ResearchException("research_malformed_response", "Research synthesizer returned invalid JSON.", ex);
        }

        if (dto is null || dto.Candidates is not { Count: > 0 })
        {
            throw new ResearchException("research_malformed_response", "Research synthesizer returned zero idea candidates.");
        }

        var sourcesById = sources.ToDictionary(s => s.Id);
        var rawClaims = (dto.Claims ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.ClaimText))
            .Select(c =>
            {
                Guid? sourceId = null;
                if (c.SourceIndex is >= 0 && c.SourceIndex.Value < sources.Count)
                {
                    sourceId = sources[c.SourceIndex.Value].Id;
                }

                return new EvidenceClaim(
                    Guid.NewGuid(),
                    packageId,
                    sourceId,
                    c.ClaimText!.Trim(),
                    string.IsNullOrWhiteSpace(c.ClaimKind) ? ClaimKinds.SynthesisHypothesis : c.ClaimKind.Trim().ToLowerInvariant(),
                    c.ConfidenceNote?.Trim(),
                    c.VisualProofSuggestion?.Trim());
            })
            .ToArray();

        var normalizedClaims = CitationPolicy.NormalizeClaims(rawClaims, sourcesById);

        var candidates = dto.Candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Title) && !string.IsNullOrWhiteSpace(c.Hook))
            .Take(5)
            .Select(c =>
            {
                var supportingIds = (c.SupportingSourceIndices ?? [])
                    .Where(idx => idx >= 0 && idx < sources.Count)
                    .Select(idx => sources[idx].Id)
                    .Distinct()
                    .ToArray();

                return new IdeaCandidate(
                    c.Title!.Trim(),
                    c.Hook!.Trim(),
                    string.IsNullOrWhiteSpace(c.TargetAudience) ? "General audience" : c.TargetAudience.Trim(),
                    string.IsNullOrWhiteSpace(c.Angle) ? "Practical deep dive" : c.Angle.Trim(),
                    string.IsNullOrWhiteSpace(c.SuggestedFormat) ? "short-explainer" : c.SuggestedFormat.Trim(),
                    string.IsNullOrWhiteSpace(c.WhyTrendingNow) ? "Active audience demand" : c.WhyTrendingNow.Trim(),
                    supportingIds,
                    NormalizeVisualStyle(c.RecommendedVisualStyle),
                    NormalizeAngleCategory(c.AngleCategory),
                    NormalizeStrategicIntent(c.StrategicIntent),
                    c.NoveltyScore is >= 0 and <= 1 ? c.NoveltyScore : null,
                    c.AtomizedShorts?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Take(3).ToArray(),
                    c.TitleVariants?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Take(3).ToArray());
            })
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new ResearchException("research_malformed_response", "Research synthesizer returned no valid idea candidates.");
        }

        PacingBlueprint? pacing = null;
        if (dto.PacingBlueprint is not null
            && !string.IsNullOrWhiteSpace(dto.PacingBlueprint.HookPattern))
        {
            pacing = new PacingBlueprint(
                dto.PacingBlueprint.HookPattern.Trim(),
                dto.PacingBlueprint.PatternInterrupts?.Trim() ?? string.Empty,
                dto.PacingBlueprint.ProofOrDemoStyle?.Trim() ?? string.Empty,
                dto.PacingBlueprint.CtaStyle?.Trim() ?? string.Empty);
        }

        var warnings = (dto.Warnings ?? [])
            .Where(w => !string.IsNullOrWhiteSpace(w.Message))
            .Select(w => new FactPolicyWarning(
                string.IsNullOrWhiteSpace(w.Severity) ? "info" : w.Severity.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(w.Category) ? "claim" : w.Category.Trim().ToLowerInvariant(),
                w.Message!.Trim()))
            .ToArray();

        return new ResearchPackage(
            packageId,
            queryTopic,
            regionCode,
            createdAt,
            providerUsed,
            sources,
            normalizedClaims,
            candidates,
            pacing,
            warnings,
            DegradedFallback: false);
    }

    public static ResearchPackage BuildDegradedFallback(
        Guid packageId,
        string queryTopic,
        string regionCode,
        DateTimeOffset createdAt,
        IReadOnlyList<ResearchSource> sources,
        string reason)
    {
        var claims = sources
            .Take(5)
            .Select(s => new EvidenceClaim(
                Guid.NewGuid(),
                packageId,
                s.Id,
                $"Observed source signal: {s.Title}",
                ClaimKinds.ObservedPattern,
                $"Fallback summary ({reason})"))
            .ToArray();

        var candidates = sources
            .Take(3)
            .Select((s, index) => new IdeaCandidate(
                s.Title,
                $"Apa yang sebenarnya terjadi di balik {s.Title}?",
                "General audience",
                "Factual breakdown from collected trend signal",
                index == 0 ? "long-explainer" : "short-vertical",
                "Collected directly from deterministic trend signal while AI synthesizer was unavailable.",
                [s.Id],
                "explainer",
                ContentAngles.StepByStepPlaybook,
                CandidateIntents.TrafficMagnet))
            .ToArray();

        return new ResearchPackage(
            packageId,
            queryTopic,
            regionCode,
            createdAt,
            "deterministic-fallback",
            sources,
            claims,
            candidates,
            null,
            [new FactPolicyWarning("warning", "provider", $"AI synthesizer unavailable ({reason}); raw collected signals preserved.")],
            DegradedFallback: true);
    }

    public static string StripMarkdownFence(string raw)
    {
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed.Trim('`').Trim();
        }

        var withoutFirst = trimmed[(firstNewline + 1)..];
        var lastFence = withoutFirst.LastIndexOf("```", StringComparison.Ordinal);
        return (lastFence >= 0 ? withoutFirst[..lastFence] : withoutFirst).Trim();
    }

    private static string NormalizeVisualStyle(string? style) =>
        style?.Trim().ToLowerInvariant() switch
        {
            "ai_image" => "ai_image",
            "motion_comic" => "motion_comic",
            _ => "explainer"
        };

    private static string NormalizeAngleCategory(string? angle)
    {
        var normalized = angle?.Trim().ToLowerInvariant();
        return ContentAngles.All.Contains(normalized) ? normalized! : ContentAngles.StepByStepPlaybook;
    }

    private static string NormalizeStrategicIntent(string? intent) =>
        string.Equals(intent?.Trim(), CandidateIntents.OfferBridge, StringComparison.OrdinalIgnoreCase)
            ? CandidateIntents.OfferBridge
            : CandidateIntents.TrafficMagnet;

    private sealed class RawSynthesisDto
    {
        public List<RawClaimDto>? Claims { get; set; }
        public List<RawCandidateDto>? Candidates { get; set; }
        public RawPacingDto? PacingBlueprint { get; set; }
        public List<RawWarningDto>? Warnings { get; set; }
    }

    private sealed class RawClaimDto
    {
        public int? SourceIndex { get; set; }
        public string? ClaimText { get; set; }
        public string? ClaimKind { get; set; }
        public string? ConfidenceNote { get; set; }
        public string? VisualProofSuggestion { get; set; }
    }

    private sealed class RawCandidateDto
    {
        public string? Title { get; set; }
        public string? Hook { get; set; }
        public string? TargetAudience { get; set; }
        public string? Angle { get; set; }
        public string? AngleCategory { get; set; }
        public string? StrategicIntent { get; set; }
        public string? SuggestedFormat { get; set; }
        public string? WhyTrendingNow { get; set; }
        public List<int>? SupportingSourceIndices { get; set; }
        public string? RecommendedVisualStyle { get; set; }
        public double? NoveltyScore { get; set; }
        public List<string>? AtomizedShorts { get; set; }
        public List<string>? TitleVariants { get; set; }
    }

    private sealed class RawPacingDto
    {
        public string? HookPattern { get; set; }
        public string? PatternInterrupts { get; set; }
        public string? ProofOrDemoStyle { get; set; }
        public string? CtaStyle { get; set; }
    }

    private sealed class RawWarningDto
    {
        public string? Severity { get; set; }
        public string? Category { get; set; }
        public string? Message { get; set; }
    }
}
