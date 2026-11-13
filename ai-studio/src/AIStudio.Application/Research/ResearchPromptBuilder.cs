using System.Text;

namespace AIStudio.Application.Research;

public static class ResearchPromptBuilder
{
    public const string SystemInstruction =
        "You are a Content Intelligence Researcher. Analyze only the provided factual trend signals " +
        "and audience comments. Never invent view counts, URLs, or dates. Return only one valid JSON object " +
        "without markdown fences or commentary.";

    public static string Build(
        ResearchRequest request,
        IReadOnlyList<ResearchSource> numberedSources,
        IReadOnlyList<TrendSignal> trimmedSignals,
        IReadOnlyList<string>? existingProjectTitles = null,
        IReadOnlyList<string>? activeGuidelines = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(numberedSources);
        ArgumentNullException.ThrowIfNull(trimmedSignals);

        var sb = new StringBuilder()
            .AppendLine("Synthesize a structured ResearchPackage from the factual signals below.")
            .Append("Topic: ").AppendLine(request.Topic)
            .Append("Region: ").AppendLine(request.RegionCode)
            .Append("Target audience: ").AppendLine(request.TargetAudience ?? "General audience")
            .Append("Offer/Product context: ").AppendLine(request.OfferContext ?? "Not specified")
            .Append("Response language: ").AppendLine(request.Language ?? "Indonesian");

        if (activeGuidelines is { Count: > 0 })
        {
            sb.AppendLine().AppendLine("Channel Guidelines (must follow):");
            foreach (var guideline in activeGuidelines.Take(5))
            {
                sb.Append("- ").AppendLine(guideline);
            }
        }

        if (existingProjectTitles is { Count: > 0 })
        {
            sb.AppendLine().AppendLine("Existing project titles (avoid duplicating; score novelty against these):");
            foreach (var title in existingProjectTitles.Take(15))
            {
                sb.Append("- ").AppendLine(title);
            }
        }

        sb.AppendLine().AppendLine("Verified Signals (use sourceIndex 0..N-1 for citations):");
        for (var i = 0; i < trimmedSignals.Count; i++)
        {
            var s = trimmedSignals[i];
            sb.Append('[').Append(i).Append("] (").Append(s.SourceType)
                .Append(", ").Append(s.FreshnessTier).Append(") ")
                .Append(s.Title);

            if (!string.IsNullOrWhiteSpace(s.ChannelOrAuthor))
            {
                sb.Append(" | by ").Append(s.ChannelOrAuthor);
            }

            if (s.Metrics.ViewCount.HasValue || s.Metrics.OutlierRatio.HasValue)
            {
                sb.Append(" | views=").Append(s.Metrics.ViewCount?.ToString() ?? "n/a")
                    .Append(", views/hr=").Append(s.Metrics.ViewsPerHour?.ToString("F1") ?? "n/a")
                    .Append(", outlier=").Append(s.Metrics.OutlierRatio?.ToString("F2") ?? "n/a")
                    .Append(", eng=").Append(s.Metrics.EngagementRate?.ToString("P2") ?? "n/a");
            }

            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(s.RawSummary))
            {
                sb.Append("    Summary: ").AppendLine(s.RawSummary);
            }

            if (s.TopComments is { Count: > 0 })
            {
                sb.AppendLine("    Top audience comments:");
                foreach (var c in s.TopComments.Take(8))
                {
                    sb.Append("      * (").Append(c.LikeCount).Append(" likes) ").AppendLine(c.Text);
                }
            }
        }

        sb.AppendLine()
            .AppendLine("Return one JSON object with these exact camelCase keys:")
            .AppendLine("- claims: array of { sourceIndex (int|null), claimText, claimKind ('verified_stat'|'observed_pattern'|'synthesis_hypothesis'), confidenceNote, visualProofSuggestion ('visual.diagram'|'visual.ai_image'|'reference_screen') }")
            .AppendLine("- candidates: 3 to 5 objects of { title, hook, targetAudience, angle, angleCategory ('myth_busting'|'reality_cost_check'|'beginner_mistakes'|'step_by_step_playbook'|'comparison_benchmark'|'case_study'), strategicIntent ('traffic_magnet'|'offer_bridge'), suggestedFormat, whyTrendingNow, supportingSourceIndices (int[]), recommendedVisualStyle ('ai_image'|'motion_comic'|'explainer'), noveltyScore (0.0-1.0), atomizedShorts (string[]), titleVariants (string[]) }")
            .AppendLine("- pacingBlueprint: { hookPattern, patternInterrupts, proofOrDemoStyle, ctaStyle }")
            .AppendLine("- warnings: array of { severity ('info'|'warning'), category ('freshness'|'policy'|'claim'), message }");

        return sb.ToString();
    }
}
