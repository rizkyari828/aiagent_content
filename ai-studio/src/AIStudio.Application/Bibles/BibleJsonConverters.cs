using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>Serializes <see cref="CharacterBibleId"/> as a plain JSON string.</summary>
public sealed class CharacterBibleIdJsonConverter : JsonConverter<CharacterBibleId>
{
    public override CharacterBibleId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!StoryIdentifier.IsValid(value))
        {
            throw new JsonException($"'{value}' is not a valid character bible id.");
        }

        return new CharacterBibleId(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        CharacterBibleId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>Serializes <see cref="CharacterBibleVersion"/> as a plain JSON integer.</summary>
public sealed class CharacterBibleVersionJsonConverter : JsonConverter<CharacterBibleVersion>
{
    public override CharacterBibleVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetInt32();
        if (value < CharacterBibleVersion.Minimum)
        {
            throw new JsonException($"A character bible version must be at least {CharacterBibleVersion.Minimum}.");
        }

        return new CharacterBibleVersion(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        CharacterBibleVersion value,
        JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

/// <summary>Serializes <see cref="WorldBibleId"/> as a plain JSON string.</summary>
public sealed class WorldBibleIdJsonConverter : JsonConverter<WorldBibleId>
{
    public override WorldBibleId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!StoryIdentifier.IsValid(value))
        {
            throw new JsonException($"'{value}' is not a valid world bible id.");
        }

        return new WorldBibleId(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        WorldBibleId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>Serializes <see cref="WorldBibleVersion"/> as a plain JSON integer.</summary>
public sealed class WorldBibleVersionJsonConverter : JsonConverter<WorldBibleVersion>
{
    public override WorldBibleVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetInt32();
        if (value < WorldBibleVersion.Minimum)
        {
            throw new JsonException($"A world bible version must be at least {WorldBibleVersion.Minimum}.");
        }

        return new WorldBibleVersion(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        WorldBibleVersion value,
        JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

/// <summary>Serializes <see cref="AssetReferenceId"/> as a plain JSON string.</summary>
public sealed class AssetReferenceIdJsonConverter : JsonConverter<AssetReferenceId>
{
    public override AssetReferenceId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!StoryIdentifier.IsValid(value))
        {
            throw new JsonException($"'{value}' is not a valid asset reference id.");
        }

        return new AssetReferenceId(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        AssetReferenceId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
