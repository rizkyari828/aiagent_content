using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Stable identifier for a character bible, such as <c>rio</c>. A validated value
/// object rather than an enum, so a future planner can introduce a new character as
/// data without recompiling the application. It names a narrative identity; it
/// never names code, an engine, a provider, or a path.
/// </summary>
[JsonConverter(typeof(CharacterBibleIdJsonConverter))]
public readonly record struct CharacterBibleId
{
    private readonly string? _value;

    public CharacterBibleId(string value)
    {
        if (!StoryIdentifier.IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid character bible id. Expected a lowercase value such as 'rio'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(CharacterBibleId)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static CharacterBibleId Parse(string value) => new(value);

    public static bool TryParse(string? value, out CharacterBibleId characterId)
    {
        if (StoryIdentifier.IsValid(value))
        {
            characterId = new CharacterBibleId(value!);
            return true;
        }

        characterId = default;
        return false;
    }

    public override string ToString() => Value;
}

/// <summary>
/// Positive version of a character bible. Part of the data identity so identity can
/// evolve intentionally without becoming a different logical character. A new
/// version never implies a new character id.
/// </summary>
[JsonConverter(typeof(CharacterBibleVersionJsonConverter))]
public readonly record struct CharacterBibleVersion
{
    public const int Minimum = 1;

    public CharacterBibleVersion(int value)
    {
        if (value < Minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A character bible version must be at least {Minimum}.");
        }

        Value = value;
    }

    public int Value { get; }

    public bool IsValid => Value >= Minimum;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
