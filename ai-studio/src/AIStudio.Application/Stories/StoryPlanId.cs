using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Stories;

/// <summary>
/// Stable identifier for a story plan, such as <c>run-ai-locally-story</c>. A
/// validated value object rather than an enum, because future planners must be
/// able to name a new story as data without a code change. It describes a
/// narrative plan; it never names code, a provider, or a path.
/// </summary>
[JsonConverter(typeof(StoryPlanIdJsonConverter))]
public readonly record struct StoryPlanId
{
    private readonly string? _value;

    public StoryPlanId(string value)
    {
        if (!StoryIdentifier.IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid story plan id. Expected a lowercase value such as 'run-ai-locally-story'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(StoryPlanId)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static StoryPlanId Parse(string value) => new(value);

    public static bool TryParse(string? value, out StoryPlanId storyPlanId)
    {
        if (StoryIdentifier.IsValid(value))
        {
            storyPlanId = new StoryPlanId(value!);
            return true;
        }

        storyPlanId = default;
        return false;
    }

    public override string ToString() => Value;
}

/// <summary>
/// Positive version of a story plan. Part of the data identity so a validated
/// loader can publish a revision without a code change. <c>default(StoryPlanVersion)</c>
/// is invalid on purpose.
/// </summary>
[JsonConverter(typeof(StoryPlanVersionJsonConverter))]
public readonly record struct StoryPlanVersion
{
    public const int Minimum = 1;

    public StoryPlanVersion(int value)
    {
        if (value < Minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A story plan version must be at least {Minimum}.");
        }

        Value = value;
    }

    public int Value { get; }

    public bool IsValid => Value >= Minimum;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
