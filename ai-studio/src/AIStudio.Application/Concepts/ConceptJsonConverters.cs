using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Concepts;

/// <summary>
/// Serializes <see cref="ConceptId"/> as a plain JSON string, so a concept manifest
/// is natural structured data for a future AI planner to return.
/// </summary>
public sealed class ConceptIdJsonConverter : JsonConverter<ConceptId>
{
    public override ConceptId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!ConceptIdentifier.IsValid(value))
        {
            throw new JsonException($"'{value}' is not a valid concept id.");
        }

        return new ConceptId(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        ConceptId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>Serializes <see cref="ConceptVersion"/> as a plain JSON integer.</summary>
public sealed class ConceptVersionJsonConverter : JsonConverter<ConceptVersion>
{
    public override ConceptVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetInt32();
        if (value < ConceptVersion.Minimum)
        {
            throw new JsonException($"A concept version must be at least {ConceptVersion.Minimum}.");
        }

        return new ConceptVersion(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        ConceptVersion value,
        JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}
