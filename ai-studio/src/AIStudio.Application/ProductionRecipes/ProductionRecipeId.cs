using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// Stable identifier for a production recipe, such as <c>tech-explainer</c>. Like
/// <c>CapabilityId</c> it is a validated value object rather than an enum, because
/// a future Concept Registry / Creative Director must be able to create a new
/// content format as data without recompiling the application for every format.
/// A recipe id describes a content format; it never names a provider class.
/// </summary>
[JsonConverter(typeof(ProductionRecipeIdJsonConverter))]
public readonly record struct ProductionRecipeId
{
    private const int MaximumLength = 64;

    private readonly string? _value;

    public ProductionRecipeId(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid production recipe id. Expected a lowercase value such as 'tech-explainer'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(ProductionRecipeId)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static ProductionRecipeId Parse(string value) => new(value);

    public static bool TryParse(string? value, out ProductionRecipeId recipeId)
    {
        if (IsValid(value))
        {
            recipeId = new ProductionRecipeId(value!);
            return true;
        }

        recipeId = default;
        return false;
    }

    /// <summary>
    /// Validates the stable form: 1..64 characters, starting with a lowercase
    /// letter, then lowercase letters, digits, '.', '_' or '-'.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            return false;
        }

        if (value[0] < 'a' || value[0] > 'z')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            var valid = (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '.'
                || character == '_'
                || character == '-';

            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Value;
}
