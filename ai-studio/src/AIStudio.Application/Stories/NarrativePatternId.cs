using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Stories;

/// <summary>
/// Stable identifier for a reusable narrative pattern, such as
/// <c>problem-solution-short</c> or <c>anime-horror-reveal</c>. A validated value
/// object rather than an enum or a director subclass: a new narrative format is
/// data and must register without a code change. It never names code, a provider,
/// or an executable.
/// </summary>
[JsonConverter(typeof(NarrativePatternIdJsonConverter))]
public readonly record struct NarrativePatternId
{
    private readonly string? _value;

    public NarrativePatternId(string value)
    {
        if (!StoryIdentifier.IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid narrative pattern id. Expected a lowercase value such as 'problem-solution-short'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(NarrativePatternId)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static NarrativePatternId Parse(string value) => new(value);

    public static bool TryParse(string? value, out NarrativePatternId patternId)
    {
        if (StoryIdentifier.IsValid(value))
        {
            patternId = new NarrativePatternId(value!);
            return true;
        }

        patternId = default;
        return false;
    }

    public override string ToString() => Value;
}

/// <summary>
/// Positive version of a narrative pattern. Part of the data identity so a pattern
/// can be revised without a code change. <c>default(NarrativePatternVersion)</c> is
/// invalid on purpose.
/// </summary>
[JsonConverter(typeof(NarrativePatternVersionJsonConverter))]
public readonly record struct NarrativePatternVersion
{
    public const int Minimum = 1;

    public NarrativePatternVersion(int value)
    {
        if (value < Minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A narrative pattern version must be at least {Minimum}.");
        }

        Value = value;
    }

    public int Value { get; }

    public bool IsValid => Value >= Minimum;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
