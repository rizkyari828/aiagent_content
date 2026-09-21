using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Stories;

/// <summary>
/// Raised when untrusted structured story output cannot be accepted as a valid
/// <see cref="StoryPlan"/>. Malformed output fails clearly; it is never repaired
/// or executed.
/// </summary>
public sealed class StoryPlanException : Exception
{
    public StoryPlanException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable story-plan parser error codes.</summary>
public static class StoryPlanParserErrorCodes
{
    public const string InvalidJson = "story_plan_invalid_json";
    public const string PlanInvalid = "story_plan_invalid";
    public const string PatternUnknown = "story_plan_pattern_unknown";
}

/// <summary>
/// Parses untrusted structured output into a validated <see cref="StoryPlan"/>. It
/// accepts strict JSON only, rejects malformed or incomplete plans, and performs no
/// speculative repair. The contract is closed: unknown members are rejected, so
/// story data cannot smuggle executable instructions (type names, commands, paths)
/// through extra fields. It prepares the same shape a future Qwen Story Director
/// would return.
/// </summary>
public static class StoryPlanParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static StoryPlan Parse(string json, INarrativePatternRegistry? patterns = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new StoryPlanException(
                StoryPlanParserErrorCodes.InvalidJson,
                "Story plan response was empty.");
        }

        StoryPlan? plan;

        try
        {
            plan = JsonSerializer.Deserialize<StoryPlan>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new StoryPlanException(
                StoryPlanParserErrorCodes.InvalidJson,
                "Story plan response is not valid JSON for a story plan.",
                exception);
        }

        if (plan is null)
        {
            throw new StoryPlanException(
                StoryPlanParserErrorCodes.InvalidJson,
                "Story plan response did not contain a story plan.");
        }

        IReadOnlyList<StoryPlanIssue> issues;

        if (patterns is null)
        {
            issues = StoryPlanValidator.Validate(plan);
        }
        else
        {
            if (!TryResolvePattern(patterns, plan, out var pattern))
            {
                throw new StoryPlanException(
                    StoryPlanParserErrorCodes.PatternUnknown,
                    $"Story plan references an unknown narrative pattern '{plan.NarrativePattern}'.");
            }

            issues = StoryPlanValidator.Validate(plan, pattern);
        }

        if (issues.Count > 0)
        {
            throw new StoryPlanException(
                StoryPlanParserErrorCodes.PlanInvalid,
                $"Story plan is not structurally valid: {issues[0].Code}.");
        }

        return plan;
    }

    private static bool TryResolvePattern(
        INarrativePatternRegistry patterns,
        StoryPlan plan,
        [NotNullWhen(true)] out NarrativePattern? pattern)
    {
        if (plan.NarrativePatternVersion.IsValid)
        {
            return patterns.TryGet(plan.NarrativePattern, plan.NarrativePatternVersion, out pattern);
        }

        return patterns.TryGetLatest(plan.NarrativePattern, out pattern);
    }
}
