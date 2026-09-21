using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Capabilities;

/// <summary>
/// Stable identifier for a production capability ("what the studio can do"), such
/// as <c>visual.diagram</c> or <c>speech.narration</c>. Deliberately a validated
/// string value object rather than an enum: future content concepts must be able
/// to request known capabilities without recompiling the application for every new
/// identifier. The identifier is data; the provider implementation behind it
/// remains trusted compile-time application code.
/// </summary>
public readonly record struct CapabilityId
{
    private readonly string? _value;

    [JsonConstructor]
    public CapabilityId(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid capability id. Expected a lowercase dotted value such as 'visual.ui_motion'.",
                nameof(value));
        }

        _value = value;
    }

    /// <summary>The stable string value; empty for <c>default(CapabilityId)</c>.</summary>
    public string Value => _value ?? string.Empty;

    public static CapabilityId Parse(string value) => new(value);

    public static bool TryParse(string? value, out CapabilityId capabilityId)
    {
        if (IsValid(value))
        {
            capabilityId = new CapabilityId(value!);
            return true;
        }

        capabilityId = default;
        return false;
    }

    /// <summary>
    /// Validates the stable dotted form: two or more lowercase segments
    /// (letters/digits/underscore, starting with a letter) joined by '.', for
    /// example <c>visual.image_to_video</c>.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var segments = value.Split('.');
        if (segments.Length < 2)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (!IsValidSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Value;

    private static bool IsValidSegment(string segment)
    {
        if (segment.Length == 0 || segment[0] < 'a' || segment[0] > 'z')
        {
            return false;
        }

        for (var index = 1; index < segment.Length; index++)
        {
            var character = segment[index];
            var valid = (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '_';

            if (!valid)
            {
                return false;
            }
        }

        return true;
    }
}
