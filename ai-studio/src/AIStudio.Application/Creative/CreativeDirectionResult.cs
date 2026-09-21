namespace AIStudio.Application.Creative;

/// <summary>
/// The Creative Director's result for one call: the proposed direction plus the
/// model that produced it. The direction is data only and is not registered,
/// approved, or produced by this layer.
/// </summary>
public sealed record CreativeDirectionResult
{
    public CreativeDirection Direction { get; init; } = new();

    public string Model { get; init; } = string.Empty;
}
