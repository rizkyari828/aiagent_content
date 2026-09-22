using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.IdentityAssets;

/// <summary>A positive version in the stable identity-asset key.</summary>
[JsonConverter(typeof(IdentityAssetVersionJsonConverter))]
public readonly record struct IdentityAssetVersion
{
    public const int Minimum = 1;

    public IdentityAssetVersion(int value)
    {
        if (value < Minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"An identity asset version must be at least {Minimum}.");
        }

        Value = value;
    }

    public int Value { get; }

    public bool IsValid => Value >= Minimum;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed class IdentityAssetVersionJsonConverter : JsonConverter<IdentityAssetVersion>
{
    public override IdentityAssetVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetInt32();
        if (value < IdentityAssetVersion.Minimum)
        {
            throw new JsonException(
                $"An identity asset version must be at least {IdentityAssetVersion.Minimum}.");
        }

        return new IdentityAssetVersion(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        IdentityAssetVersion value,
        JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}
