namespace AIStudio.Application.Capabilities;

/// <summary>
/// Metadata for one production capability ("what the studio can do"), independent
/// of which provider implements it. No executable information is stored here.
/// </summary>
public sealed record CapabilityDescriptor
{
    public CapabilityId Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Coarse grouping such as speech, music, visual, media, subtitle.</summary>
    public string Category { get; init; } = string.Empty;
}
