using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Stories;

/// <summary>
/// Stable identifier for one story beat inside a plan, such as <c>beat-03</c>.
/// Beats are the narrative progression; the id stays data and never names code.
/// </summary>
[JsonConverter(typeof(StoryBeatIdJsonConverter))]
public readonly record struct StoryBeatId
{
    private readonly string? _value;

    public StoryBeatId(string value)
    {
        if (!StoryIdentifier.IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid story beat id. Expected a lowercase value such as 'beat-03'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(StoryBeatId)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static StoryBeatId Parse(string value) => new(value);

    public static bool TryParse(string? value, out StoryBeatId beatId)
    {
        if (StoryIdentifier.IsValid(value))
        {
            beatId = new StoryBeatId(value!);
            return true;
        }

        beatId = default;
        return false;
    }

    public override string ToString() => Value;
}

/// <summary>
/// The narrative ROLE a beat plays, such as <c>hook</c>, <c>obstacle</c>,
/// <c>tutorial-step</c>, or <c>chorus</c>. Deliberately a validated string value
/// object rather than an enum: new narrative formats must be representable as data
/// without recompiling the application. A role is narrative intent only.
/// </summary>
[JsonConverter(typeof(StoryBeatRoleJsonConverter))]
public readonly record struct StoryBeatRole
{
    private readonly string? _value;

    public StoryBeatRole(string value)
    {
        if (!StoryIdentifier.IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid beat role. Expected a lowercase value such as 'hook' or 'emotional-turn'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(StoryBeatRole)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static StoryBeatRole Parse(string value) => new(value);

    public static bool TryParse(string? value, out StoryBeatRole role)
    {
        if (StoryIdentifier.IsValid(value))
        {
            role = new StoryBeatRole(value!);
            return true;
        }

        role = default;
        return false;
    }

    public override string ToString() => Value;
}
