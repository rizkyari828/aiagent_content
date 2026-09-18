namespace AIStudio.Application.Rendering;

/// <summary>
/// Readable subtitle styling. Colors are intentionally fixed to high-contrast
/// white-on-black and no font file path is hard-coded: libass resolves
/// <see cref="FontName"/> through the platform font configuration and falls back
/// to its default when the font is absent.
/// </summary>
public sealed class SubtitleStyle
{
    public string FontName { get; set; } = "DejaVu Sans";

    public int FontSize { get; set; } = 16;

    public int Outline { get; set; } = 1;

    public int Shadow { get; set; } = 1;

    /// <summary>Safe bottom margin in libass script units.</summary>
    public int MarginVertical { get; set; } = 18;
}
