namespace AIStudio.Application.Creative;

/// <summary>
/// Optional user hints for the creative direction. They are preferences, not a
/// second concept definition: format/style names stay declarative data, and the
/// Creative Director remains free to choose differently unless constrained.
/// </summary>
public sealed record CreativeConstraints
{
    public string? PreferredFormat { get; init; }

    public string? PreferredStyle { get; init; }

    public int? TargetDurationSeconds { get; init; }
}
