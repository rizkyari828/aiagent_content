using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Concepts;

/// <summary>
/// Stable identifier for a content concept, such as <c>local-ai-anime-short</c>.
/// A validated value object rather than an enum, because a future AI planner must
/// be able to name a new concept as data without a code change. A concept id
/// describes what we are trying to make; it never names code, a provider, or a
/// path.
/// </summary>
[JsonConverter(typeof(ConceptIdJsonConverter))]
public readonly record struct ConceptId
{
    private readonly string? _value;

    public ConceptId(string value)
    {
        if (!ConceptIdentifier.IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid concept id. Expected a lowercase value such as 'local-ai-tech-explainer'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(ConceptId)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static ConceptId Parse(string value) => new(value);

    public static bool TryParse(string? value, out ConceptId conceptId)
    {
        if (ConceptIdentifier.IsValid(value))
        {
            conceptId = new ConceptId(value!);
            return true;
        }

        conceptId = default;
        return false;
    }

    public override string ToString() => Value;
}
