using System.Text;

namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Pure, deterministic SVG composer for scene visuals. It contains no FFmpeg or
/// process knowledge; rasterization is handled behind
/// <see cref="ISceneVisualRenderer"/>. Composed visuals use generous safe margins
/// so the renderer's centered-zoom default never crops meaningful content.
/// </summary>
public static class SceneVisualSvg
{
    public const int Width = 1280;
    public const int Height = 720;

    private static readonly IReadOnlyDictionary<SceneVisualPalette, Palette> Palettes =
        new Dictionary<SceneVisualPalette, Palette>
        {
            [SceneVisualPalette.Ocean] = new("#0b1e2d", "#123a4d", "#5fd3c4", "#eaf6ff"),
            [SceneVisualPalette.Sunset] = new("#2a1220", "#4a1f2d", "#ffb26b", "#fff2e8"),
            [SceneVisualPalette.Violet] = new("#14122b", "#2a2352", "#b9a7ff", "#f0edff"),
            [SceneVisualPalette.Slate] = new("#101418", "#1e2a33", "#7fb2ff", "#eaf1fb")
        };

    public static string Compose(SceneVisualBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        var palette = Palettes[brief.Palette];

        var sb = new StringBuilder(8_192);
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\">");
        sb.Append("<defs>");
        sb.Append($"<linearGradient id=\"bg\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"{palette.From}\"/><stop offset=\"1\" stop-color=\"{palette.To}\"/></linearGradient>");
        sb.Append("</defs>");
        sb.Append($"<rect width=\"{Width}\" height=\"{Height}\" fill=\"url(#bg)\"/>");
        Decorate(sb, palette);
        Header(sb, brief, palette);

        switch (brief.Layout)
        {
            case SceneVisualLayout.Hero:
                Hero(sb, brief, palette);
                break;
            case SceneVisualLayout.Cards:
                Cards(sb, brief, palette);
                break;
            case SceneVisualLayout.Window:
                Window(sb, brief, palette);
                break;
            case SceneVisualLayout.Chat:
                Chat(sb, brief, palette);
                break;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void Decorate(StringBuilder sb, Palette palette)
    {
        sb.Append($"<circle cx=\"1180\" cy=\"80\" r=\"220\" fill=\"{palette.Accent}\" fill-opacity=\"0.06\"/>");
        sb.Append($"<circle cx=\"60\" cy=\"690\" r=\"180\" fill=\"{palette.Accent}\" fill-opacity=\"0.05\"/>");
    }

    private static void Header(StringBuilder sb, SceneVisualBrief brief, Palette palette)
    {
        Text(sb, 88, 104, 20, "bold", palette.Accent, brief.Kicker.ToUpperInvariant(), letterSpacing: 5);
        Text(sb, 88, 176, 50, "bold", palette.Text, brief.Heading);
        Rect(sb, 88, 198, 96, 6, 3, palette.Accent);
    }

    private static void Hero(StringBuilder sb, SceneVisualBrief brief, Palette palette)
    {
        Rect(sb, 140, 250, 1000, 310, 28, "#ffffff", fillOpacity: 0.05, stroke: palette.Accent, strokeOpacity: 0.3);

        var hero = brief.Cards.Count > 0 ? brief.Cards[0] : new SceneVisualCard(brief.Heading);
        Icon(sb, hero.Icon, 320, 405, 190, palette.Accent);
        Text(sb, 470, 385, 36, "bold", palette.Text, hero.Title);
        var detail = hero.Detail ?? brief.Note;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var lines = Wrap(detail!, 30);
            for (var i = 0; i < lines.Count; i++)
            {
                Text(sb, 470, 435 + i * 34, 23, "normal", palette.Text, lines[i], opacity: 0.85);
            }
        }

        var chips = brief.Cards.Skip(1).Select(card => card.Title).ToArray();
        if (chips.Length > 0)
        {
            var total = chips.Sum(ChipWidth) + (chips.Length - 1) * 18;
            var x = (Width - total) / 2;
            foreach (var chip in chips)
            {
                Chip(sb, x, 536, chip, palette.Accent, palette.Text);
                x += ChipWidth(chip) + 18;
            }
        }
    }

    private static void Cards(StringBuilder sb, SceneVisualBrief brief, Palette palette)
    {
        var count = brief.Cards.Count;
        var compact = count >= 4;
        var columns = count >= 3 && count != 4 ? 3 : 2;
        var cardWidth = columns == 3 ? 346 : 540;
        var cardHeight = compact ? 152 : 300;
        var gapX = columns == 3 ? 41 : 40;
        var gapY = 24;
        var originX = 80;
        var originY = 250;

        for (var index = 0; index < count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var x = originX + column * (cardWidth + gapX);
            var y = originY + row * (cardHeight + gapY);
            var card = brief.Cards[index];

            Rect(sb, x, y, cardWidth, cardHeight, 22, "#ffffff", fillOpacity: 0.05, stroke: palette.Accent, strokeOpacity: 0.25);

            if (compact)
            {
                IconBadge(sb, x + 26, y + 26, 56, card.Icon, palette);
                Text(sb, x + 104, y + 66, 26, "bold", palette.Text, card.Title);
                if (!string.IsNullOrWhiteSpace(card.Detail))
                {
                    Text(sb, x + 104, y + 106, 18, "normal", palette.Text, Wrap(card.Detail!, 30)[0], opacity: 0.8);
                }
            }
            else
            {
                IconBadge(sb, x + 32, y + 30, 64, card.Icon, palette);
                Text(sb, x + 32, y + 150, 30, "bold", palette.Text, card.Title);
                if (!string.IsNullOrWhiteSpace(card.Detail))
                {
                    var lines = Wrap(card.Detail!, columns == 3 ? 22 : 34);
                    for (var i = 0; i < lines.Count && i < 3; i++)
                    {
                        Text(sb, x + 32, y + 190 + i * 28, 20, "normal", palette.Text, lines[i], opacity: 0.8);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(brief.Note))
        {
            Text(sb, Width / 2, 604, 22, "normal", palette.Accent, brief.Note!, anchor: "middle");
        }
    }

    private static void Window(StringBuilder sb, SceneVisualBrief brief, Palette palette)
    {
        Frame(sb, 160, 235, 960, 370, "Terminal", palette);
        Icon(sb, SceneVisualIcon.Download, 1030, 320, 58, palette.Accent);

        var y = 350;
        if (!string.IsNullOrWhiteSpace(brief.Command))
        {
            Text(sb, 200, y, 26, "bold", palette.Accent, "$", family: Mono);
            Text(sb, 232, y, 24, "normal", palette.Text, brief.Command!, family: Mono);
            y += 70;
        }

        if (brief.Progress > 0)
        {
            var clamped = Math.Clamp(brief.Progress, 0, 1);
            Rect(sb, 200, y + 20, 880, 16, 8, "#ffffff", fillOpacity: 0.08);
            Rect(sb, 200, y + 20, (int)(880 * clamped), 16, 8, palette.Accent);
            Text(sb, 1080, y + 14, 20, "bold", palette.Accent, $"{clamped * 100:0}%", anchor: "end");
            y += 80;
        }

        if (!string.IsNullOrWhiteSpace(brief.Note))
        {
            Text(sb, 200, y + 50, 24, "normal", palette.Text, brief.Note!, opacity: 0.85);
        }
    }

    private static void Chat(StringBuilder sb, SceneVisualBrief brief, Palette palette)
    {
        Frame(sb, 240, 205, 800, 395, brief.Note ?? "AI Lokal", palette);
        sb.Append($"<circle cx=\"962\" cy=\"232\" r=\"7\" fill=\"#5ef2a0\"/>");
        Text(sb, 946, 238, 18, "normal", palette.Text, "offline", anchor: "end", opacity: 0.75);

        var y = 300;
        if (brief.Cards.Count > 0)
        {
            y = Bubble(sb, 990, y, brief.Cards[0].Title, palette.Accent, "#0b0f14", rightAligned: true);
        }

        if (brief.Cards.Count > 1)
        {
            Bubble(sb, 290, y + 24, brief.Cards[1].Title, "#ffffff", palette.Text, rightAligned: false, fillOpacity: 0.12);
        }
    }

    private static int Bubble(
        StringBuilder sb,
        int x,
        int y,
        string text,
        string fill,
        string textFill,
        bool rightAligned,
        double fillOpacity = 0.85)
    {
        var maxWidth = 460;
        var lines = Wrap(text, 30);
        var lineHeight = 32;
        var height = lines.Count * lineHeight + 28;
        var width = Math.Min(maxWidth, lines.Max(line => ApproximateWidth(line, 22)) + 44);
        var left = rightAligned ? x - width : x;

        Rect(sb, left, y, width, height, 18, fill, fillOpacity: fillOpacity);
        for (var i = 0; i < lines.Count; i++)
        {
            Text(sb, left + 22, y + 40 + i * lineHeight, 22, "normal", textFill, lines[i]);
        }

        return y + height;
    }

    private static void Frame(StringBuilder sb, int x, int y, int w, int h, string title, Palette palette)
    {
        Rect(sb, x, y, w, h, 22, "#05090d", fillOpacity: 0.92, stroke: palette.Accent, strokeOpacity: 0.35);
        Rect(sb, x, y, w, 54, 22, "#ffffff", fillOpacity: 0.06);
        sb.Append($"<rect x=\"{x}\" y=\"{y + 40}\" width=\"{w}\" height=\"14\" fill=\"#05090d\" fill-opacity=\"0.92\"/>");
        foreach (var (cx, color) in new[] { (x + 38, "#ff5f56"), (x + 66, "#ffbd2e"), (x + 94, "#27c93f") })
        {
            sb.Append($"<circle cx=\"{cx}\" cy=\"{y + 27}\" r=\"7\" fill=\"{color}\"/>");
        }

        Text(sb, x + 126, y + 34, 20, "normal", palette.Text, title, opacity: 0.75);
    }

    private static void IconBadge(StringBuilder sb, int x, int y, int size, SceneVisualIcon icon, Palette palette)
    {
        Rect(sb, x, y, size, size, 16, palette.Accent, fillOpacity: 0.14);
        Icon(sb, icon, x + size / 2, y + size / 2, size * 0.62, palette.Accent);
    }

    private static void Chip(StringBuilder sb, int x, int y, string label, string accent, string text)
    {
        var width = ChipWidth(label);
        Rect(sb, x, y, width, 46, 23, "#ffffff", fillOpacity: 0.08, stroke: accent, strokeOpacity: 0.4);
        Text(sb, x + width / 2, y + 30, 20, "medium", text, label, anchor: "middle");
    }

    private static int ChipWidth(string label) => ApproximateWidth(label, 20) + 44;

    private static void Icon(StringBuilder sb, SceneVisualIcon icon, int cx, int cy, double size, string color)
    {
        var s = size;
        switch (icon)
        {
            case SceneVisualIcon.Laptop:
                Rect(sb, cx - s * 0.45, cy - s * 0.45, s * 0.9, s * 0.62, s * 0.08, color, fillOpacity: 0.16, stroke: color, strokeWidth: 4);
                Rect(sb, cx - s * 0.58, cy + s * 0.18, s * 1.16, s * 0.09, s * 0.03, color);
                Rect(sb, cx - s * 0.3, cy - s * 0.3, s * 0.6, s * 0.06, s * 0.02, color, fillOpacity: 0.6);
                break;
            case SceneVisualIcon.Cloud:
                sb.Append($"<circle cx=\"{cx - s * 0.24}\" cy=\"{cy + s * 0.04}\" r=\"{s * 0.22}\" fill=\"{color}\"/>");
                sb.Append($"<circle cx=\"{cx}\" cy=\"{cy - s * 0.08}\" r=\"{s * 0.28}\" fill=\"{color}\"/>");
                sb.Append($"<circle cx=\"{cx + s * 0.27}\" cy=\"{cy + s * 0.04}\" r=\"{s * 0.2}\" fill=\"{color}\"/>");
                Rect(sb, cx - s * 0.45, cy + s * 0.02, s * 0.9, s * 0.22, s * 0.06, color);
                sb.Append($"<line x1=\"{cx - s * 0.5}\" y1=\"{cy - s * 0.45}\" x2=\"{cx + s * 0.5}\" y2=\"{cy + s * 0.42}\" stroke=\"#ff6b6b\" stroke-width=\"{s * 0.09}\" stroke-linecap=\"round\"/>");
                break;
            case SceneVisualIcon.Check:
                sb.Append($"<polyline points=\"{cx - s * 0.32},{cy} {cx - s * 0.05},{cy + s * 0.3} {cx + s * 0.38},{cy - s * 0.32}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{s * 0.14}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                break;
            case SceneVisualIcon.Model:
                Rect(sb, cx - s * 0.4, cy - s * 0.42, s * 0.8, s * 0.24, s * 0.05, color, fillOpacity: 0.25, stroke: color, strokeWidth: 3);
                Rect(sb, cx - s * 0.4, cy - s * 0.1, s * 0.8, s * 0.24, s * 0.05, color, fillOpacity: 0.45, stroke: color, strokeWidth: 3);
                Rect(sb, cx - s * 0.4, cy + s * 0.22, s * 0.8, s * 0.24, s * 0.05, color, fillOpacity: 0.7, stroke: color, strokeWidth: 3);
                break;
            case SceneVisualIcon.Chat:
                Rect(sb, cx - s * 0.45, cy - s * 0.4, s * 0.9, s * 0.62, s * 0.14, color, fillOpacity: 0.2, stroke: color, strokeWidth: 4);
                sb.Append($"<path d=\"M {cx - s * 0.16} {cy + s * 0.22} L {cx - s * 0.02} {cy + s * 0.46} L {cx + s * 0.14} {cy + s * 0.22} Z\" fill=\"{color}\" fill-opacity=\"0.2\"/>");
                break;
            case SceneVisualIcon.Download:
                sb.Append($"<line x1=\"{cx}\" y1=\"{cy - s * 0.42}\" x2=\"{cx}\" y2=\"{cy + s * 0.1}\" stroke=\"{color}\" stroke-width=\"{s * 0.12}\" stroke-linecap=\"round\"/>");
                sb.Append($"<polyline points=\"{cx - s * 0.24},{cy - s * 0.06} {cx},{cy + s * 0.18} {cx + s * 0.24},{cy - s * 0.06}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{s * 0.12}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                sb.Append($"<polyline points=\"{cx - s * 0.4},{cy + s * 0.34} {cx - s * 0.4},{cy + s * 0.44} {cx + s * 0.4},{cy + s * 0.44} {cx + s * 0.4},{cy + s * 0.34}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{s * 0.1}\" stroke-linecap=\"round\"/>");
                break;
            case SceneVisualIcon.Shield:
                sb.Append($"<path d=\"M {cx} {cy - s * 0.45} L {cx + s * 0.38} {cy - s * 0.28} L {cx + s * 0.38} {cy + s * 0.08} Q {cx + s * 0.38} {cy + s * 0.36} {cx} {cy + s * 0.46} Q {cx - s * 0.38} {cy + s * 0.36} {cx - s * 0.38} {cy + s * 0.08} L {cx - s * 0.38} {cy - s * 0.28} Z\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{s * 0.1}\" stroke-linejoin=\"round\"/>");
                break;
            case SceneVisualIcon.Bolt:
                sb.Append($"<polygon points=\"{cx + s * 0.08},{cy - s * 0.46} {cx - s * 0.34},{cy + s * 0.06} {cx - s * 0.02},{cy + s * 0.06} {cx - s * 0.12},{cy + s * 0.46} {cx + s * 0.34},{cy - s * 0.08} {cx + s * 0.02},{cy - s * 0.08}\" fill=\"{color}\"/>");
                break;
            case SceneVisualIcon.Bulb:
                sb.Append($"<circle cx=\"{cx}\" cy=\"{cy - s * 0.08}\" r=\"{s * 0.32}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{s * 0.1}\"/>");
                Rect(sb, cx - s * 0.14, cy + s * 0.24, s * 0.28, s * 0.2, s * 0.04, color);
                break;
            case SceneVisualIcon.Disk:
                Rect(sb, cx - s * 0.42, cy - s * 0.42, s * 0.84, s * 0.84, s * 0.1, color, fillOpacity: 0.18, stroke: color, strokeWidth: 4);
                sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{s * 0.2}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{s * 0.08}\"/>");
                Rect(sb, cx - s * 0.3, cy - s * 0.34, s * 0.6, s * 0.14, s * 0.03, color, fillOpacity: 0.6);
                break;
        }
    }

    private static void Text(
        StringBuilder sb,
        int x,
        int y,
        int size,
        string weight,
        string fill,
        string content,
        string anchor = "start",
        string family = "DejaVu Sans",
        double opacity = 1,
        int letterSpacing = 0)
    {
        sb.Append($"<text x=\"{x}\" y=\"{y}\" font-family=\"{family}\" font-size=\"{size}\" font-weight=\"{weight}\" fill=\"{fill}\" fill-opacity=\"{opacity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\"");
        if (anchor != "start")
        {
            sb.Append($" text-anchor=\"{anchor}\"");
        }

        if (letterSpacing > 0)
        {
            sb.Append($" letter-spacing=\"{letterSpacing}\"");
        }

        sb.Append($">{Escape(content)}</text>");
    }

    private static void Rect(
        StringBuilder sb,
        double x,
        double y,
        double w,
        double h,
        double rx,
        string fill,
        double fillOpacity = 1,
        string? stroke = null,
        double strokeOpacity = 1,
        double strokeWidth = 2)
    {
        sb.Append($"<rect x=\"{Round(x)}\" y=\"{Round(y)}\" width=\"{Round(w)}\" height=\"{Round(h)}\" rx=\"{Round(rx)}\" fill=\"{fill}\" fill-opacity=\"{Round(fillOpacity)}\"");
        if (stroke is not null)
        {
            sb.Append($" stroke=\"{stroke}\" stroke-opacity=\"{Round(strokeOpacity)}\" stroke-width=\"{Round(strokeWidth)}\"");
        }

        sb.Append("/>");
    }

    private const string Mono = "DejaVu Sans Mono";

    private static int ApproximateWidth(string text, int fontSize) => (int)(text.Length * fontSize * 0.56);

    private static IReadOnlyList<string> Wrap(string text, int maxChars)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines.Count == 0 ? [text] : lines;
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string Round(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record Palette(string From, string To, string Accent, string Text);
}
