using AIStudio.Application.Jobs.GenerateIdea;

namespace AIStudio.Application.Creative;

/// <summary>
/// The already-selected, approved idea the Creative Director consumes. It answers
/// WHAT idea is worth making (topic, angle, audience, objective, hook premise);
/// the Creative Director decides HOW to express it. Idea generation, trend
/// discovery, and ranking stay upstream in the Solution Idea / GenerateIdea layer.
/// </summary>
public sealed record ApprovedIdea
{
    /// <summary>Optional trace back to the approved idea (for example a job or idea id).</summary>
    public string? IdeaReference { get; init; }

    public string Topic { get; init; } = string.Empty;

    public string Angle { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string? Objective { get; init; }

    public string HookPremise { get; init; } = string.Empty;

    public string? SourceSummary { get; init; }

    public string? Language { get; init; }

    public CreativeConstraints Constraints { get; init; } = new();

    /// <summary>
    /// Maps the existing GenerateIdea result into this neutral boundary. The idea
    /// generation layer stays authoritative for WHAT the idea is; only descriptive
    /// fields are copied, never job/infrastructure concerns.
    /// </summary>
    public static ApprovedIdea FromGenerateIdeaResult(
        GenerateIdeaResult result,
        string? ideaReference = null,
        string? objective = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ApprovedIdea
        {
            IdeaReference = ideaReference,
            Topic = result.Title,
            Angle = result.Angle,
            Audience = result.TargetAudience,
            Objective = objective,
            HookPremise = result.Hook,
            SourceSummary = result.Summary,
            Constraints = new CreativeConstraints
            {
                PreferredFormat = result.SuggestedFormat
            }
        };
    }
}
