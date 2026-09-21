using System.Globalization;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Concepts;

/// <summary>
/// Positive version of a concept manifest. Part of the data identity so a
/// validated loader can publish a new revision of a concept without a code
/// change. <c>default(ConceptVersion)</c> is invalid on purpose.
/// </summary>
[JsonConverter(typeof(ConceptVersionJsonConverter))]
public readonly record struct ConceptVersion
{
    public const int Minimum = 1;

    public ConceptVersion(int value)
    {
        if (value < Minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A concept version must be at least {Minimum}.");
        }

        Value = value;
    }

    public int Value { get; }

    public bool IsValid => Value >= Minimum;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
