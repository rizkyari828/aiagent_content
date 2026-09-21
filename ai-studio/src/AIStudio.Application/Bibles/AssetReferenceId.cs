using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Stable identifier for a reusable asset, such as <c>character-rio-front-v1</c>.
/// A bible references assets by this id only — never a filesystem path, URL, or
/// provider blob. A future Asset Registry resolves the id to real storage. The id
/// is engine-neutral: it may describe an image, a turnaround, a voice reference, or
/// a 3D model without the bible knowing which.
/// </summary>
[JsonConverter(typeof(AssetReferenceIdJsonConverter))]
public readonly record struct AssetReferenceId
{
    private readonly string? _value;

    public AssetReferenceId(string value)
    {
        if (!StoryIdentifier.IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid asset reference id. Expected a lowercase value such as 'character-rio-front-v1'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(AssetReferenceId)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static AssetReferenceId Parse(string value) => new(value);

    public static bool TryParse(string? value, out AssetReferenceId assetId)
    {
        if (StoryIdentifier.IsValid(value))
        {
            assetId = new AssetReferenceId(value!);
            return true;
        }

        assetId = default;
        return false;
    }

    public override string ToString() => Value;
}
