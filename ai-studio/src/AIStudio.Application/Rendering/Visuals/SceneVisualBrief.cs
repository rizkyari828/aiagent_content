namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Layout template for a composed scene visual. Kept deliberately small: one
/// layout per communication pattern needed by the Video #1 storyboard.
/// </summary>
public enum SceneVisualLayout
{
    Hero,
    Cards,
    Window,
    Chat
}

/// <summary>Controlled palettes so composed visuals stay visually consistent.</summary>
public enum SceneVisualPalette
{
    Ocean,
    Sunset,
    Violet,
    Slate
}

/// <summary>Small built-in icon set for composed visuals.</summary>
public enum SceneVisualIcon
{
    None,
    Laptop,
    Cloud,
    Check,
    Model,
    Chat,
    Download,
    Shield,
    Bolt,
    Bulb,
    Disk
}

public sealed record SceneVisualCard(
    string Title,
    string? Detail = null,
    SceneVisualIcon Icon = SceneVisualIcon.None);

/// <summary>
/// Structured creative direction for one scene visual. This is what a planner
/// (human or Qwen) supplies; the composer turns it into SVG and the rasterizer
/// into a PNG. The renderer never consumes this type directly.
/// </summary>
public sealed record SceneVisualBrief(
    int SceneIndex,
    string Kicker,
    string Heading,
    SceneVisualLayout Layout,
    SceneVisualPalette Palette,
    IReadOnlyList<SceneVisualCard> Cards,
    string? Note = null,
    string? Command = null,
    double Progress = 0,
    bool Typing = false,
    bool Completion = false,
    string? VisualDescription = null);
