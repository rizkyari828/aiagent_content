using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Stable identifier for a world bible, such as <c>rio-bedroom</c>. A validated
/// value object rather than an enum, so a future planner can introduce a new
/// environment as data without recompiling the application. It names a world
/// identity; it never names a renderer, provider, or path.
/// </summary>
[JsonConverter(typeof(WorldBibleIdJsonConverter))]
public readonly record struct WorldBibleId
{
    private readonly string? _value;

    public WorldBibleId(string value)
    {
        if (!StoryIdentifier.IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid world bible id. Expected a lowercase value such as 'rio-bedroom'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(WorldBibleId)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static WorldBibleId Parse(string value) => new(value);

    public static bool TryParse(string? value, out WorldBibleId worldId)
    {
        if (StoryIdentifier.IsValid(value))
        {
            worldId = new WorldBibleId(value!);
            return true;
        }

        worldId = default;
        return false;
    }

    public override string ToString() => Value;
}

/// <summary>
/// Positive version of a world bible. Part of the data identity so a world's stable
/// identity can be refined without becoming a different environment id.
/// </summary>
[JsonConverter(typeof(WorldBibleVersionJsonConverter))]
public readonly record struct WorldBibleVersion
{
    public const int Minimum = 1;

    public WorldBibleVersion(int value)
    {
        if (value < Minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A world bible version must be at least {Minimum}.");
        }

        Value = value;
    }

    public int Value { get; }

    public bool IsValid => Value >= Minimum;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
