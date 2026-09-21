namespace AIStudio.Application.Creative;

/// <summary>
/// Per-request options for the Creative Director. The approved idea remains
/// authoritative; these only tune the call, not the idea.
/// </summary>
public sealed record CreativeDirectionOptions
{
    /// <summary>Optional model override; null uses the configured default.</summary>
    public string? Model { get; init; }

    /// <summary>
    /// When true, the director may propose a style/format that exceeds current
    /// capabilities (resolution still reports it honestly). Default true keeps
    /// creative ambition possible; set false to require a currently resolvable idea.
    /// </summary>
    public bool AllowBeyondCurrentCapabilities { get; init; } = true;
}
