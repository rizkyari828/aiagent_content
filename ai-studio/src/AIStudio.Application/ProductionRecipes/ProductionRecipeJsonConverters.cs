using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// Serializes <see cref="ProductionRecipeId"/> as a plain JSON string, so a recipe
/// (or a future Qwen-generated concept referencing one) stays natural data rather
/// than a wrapper object.
/// </summary>
public sealed class ProductionRecipeIdJsonConverter : JsonConverter<ProductionRecipeId>
{
    public override ProductionRecipeId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!ProductionRecipeId.IsValid(value))
        {
            throw new JsonException(
                $"'{value}' is not a valid production recipe id.");
        }

        return new ProductionRecipeId(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        ProductionRecipeId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>Serializes <see cref="ProductionRecipeVersion"/> as a plain JSON integer.</summary>
public sealed class ProductionRecipeVersionJsonConverter : JsonConverter<ProductionRecipeVersion>
{
    public override ProductionRecipeVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetInt32();
        if (value < ProductionRecipeVersion.Minimum)
        {
            throw new JsonException(
                $"A production recipe version must be at least {ProductionRecipeVersion.Minimum}.");
        }

        return new ProductionRecipeVersion(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        ProductionRecipeVersion value,
        JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}
