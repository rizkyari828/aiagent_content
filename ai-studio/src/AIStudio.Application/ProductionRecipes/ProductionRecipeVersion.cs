using System.Globalization;
using System.Text.Json.Serialization;

namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// Positive version of a production recipe. The integer is the identity fragment,
/// so a validated data loader can later publish a new revision without a code
/// change. <c>default(ProductionRecipeVersion)</c> is invalid on purpose.
/// </summary>
public readonly record struct ProductionRecipeVersion
{
    public const int Minimum = 1;

    [JsonConstructor]
    public ProductionRecipeVersion(int value)
    {
        if (value < Minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A production recipe version must be at least {Minimum}.");
        }

        Value = value;
    }

    public int Value { get; }

    public bool IsValid => Value >= Minimum;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
