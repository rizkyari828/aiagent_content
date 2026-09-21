using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Stories;

/// <summary>Serializes <see cref="StoryPlanId"/> as a plain JSON string.</summary>
public sealed class StoryPlanIdJsonConverter : JsonConverter<StoryPlanId>
{
    public override StoryPlanId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!StoryIdentifier.IsValid(value))
        {
            throw new JsonException($"'{value}' is not a valid story plan id.");
        }

        return new StoryPlanId(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        StoryPlanId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>Serializes <see cref="StoryPlanVersion"/> as a plain JSON integer.</summary>
public sealed class StoryPlanVersionJsonConverter : JsonConverter<StoryPlanVersion>
{
    public override StoryPlanVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetInt32();
        if (value < StoryPlanVersion.Minimum)
        {
            throw new JsonException($"A story plan version must be at least {StoryPlanVersion.Minimum}.");
        }

        return new StoryPlanVersion(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        StoryPlanVersion value,
        JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

/// <summary>Serializes <see cref="NarrativePatternId"/> as a plain JSON string.</summary>
public sealed class NarrativePatternIdJsonConverter : JsonConverter<NarrativePatternId>
{
    public override NarrativePatternId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!StoryIdentifier.IsValid(value))
        {
            throw new JsonException($"'{value}' is not a valid narrative pattern id.");
        }

        return new NarrativePatternId(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        NarrativePatternId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>Serializes <see cref="NarrativePatternVersion"/> as a plain JSON integer.</summary>
public sealed class NarrativePatternVersionJsonConverter : JsonConverter<NarrativePatternVersion>
{
    public override NarrativePatternVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetInt32();
        if (value < NarrativePatternVersion.Minimum)
        {
            throw new JsonException(
                $"A narrative pattern version must be at least {NarrativePatternVersion.Minimum}.");
        }

        return new NarrativePatternVersion(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        NarrativePatternVersion value,
        JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

/// <summary>Serializes <see cref="StoryBeatId"/> as a plain JSON string.</summary>
public sealed class StoryBeatIdJsonConverter : JsonConverter<StoryBeatId>
{
    public override StoryBeatId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!StoryIdentifier.IsValid(value))
        {
            throw new JsonException($"'{value}' is not a valid story beat id.");
        }

        return new StoryBeatId(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        StoryBeatId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>Serializes <see cref="StoryBeatRole"/> as a plain JSON string.</summary>
public sealed class StoryBeatRoleJsonConverter : JsonConverter<StoryBeatRole>
{
    public override StoryBeatRole Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!StoryIdentifier.IsValid(value))
        {
            throw new JsonException($"'{value}' is not a valid beat role.");
        }

        return new StoryBeatRole(value!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        StoryBeatRole value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
