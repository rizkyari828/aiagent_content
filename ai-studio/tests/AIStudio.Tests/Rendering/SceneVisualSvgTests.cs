using AIStudio.Application.Rendering.Visuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class SceneVisualSvgTests
{
    [Fact]
    public void Compose_HeroProducesCanvasWithHeadingAndChips()
    {
        var svg = SceneVisualSvg.Compose(new SceneVisualBrief(
            0,
            "Video 1",
            "AI di Laptop Tanpa Internet",
            SceneVisualLayout.Hero,
            SceneVisualPalette.Ocean,
            [
                new SceneVisualCard("Tanpa Internet", "Jalan penuh offline di laptopmu", SceneVisualIcon.Laptop),
                new SceneVisualCard("Privasi"),
                new SceneVisualCard("Gratis")
            ],
            Note: "Privasi aman"));

        Assert.StartsWith("<svg", svg);
        Assert.EndsWith("</svg>", svg);
        Assert.Contains("width=\"1280\"", svg);
        Assert.Contains("height=\"720\"", svg);
        Assert.Contains("AI di Laptop Tanpa Internet", svg);
        Assert.Contains("VIDEO 1", svg);
        Assert.Contains("Privasi", svg);
        Assert.Contains("Gratis", svg);
    }

    [Fact]
    public void Compose_CardsRendersEveryCard()
    {
        var svg = SceneVisualSvg.Compose(new SceneVisualBrief(
            2,
            "Kebutuhan",
            "Yang Kamu Butuhkan",
            SceneVisualLayout.Cards,
            SceneVisualPalette.Violet,
            [
                new SceneVisualCard("Laptop", "Minimal RAM 8 GB", SceneVisualIcon.Laptop),
                new SceneVisualCard("Ruang Disk", "Sekitar 5 GB", SceneVisualIcon.Disk),
                new SceneVisualCard("Koneksi", "Sekali untuk unduh model", SceneVisualIcon.Download)
            ]));

        Assert.Contains("Laptop", svg);
        Assert.Contains("Ruang Disk", svg);
        Assert.Contains("Koneksi", svg);
        Assert.Contains("Minimal RAM 8 GB", svg);
    }

    [Fact]
    public void Compose_WindowRendersCommandProgressAndNote()
    {
        var svg = SceneVisualSvg.Compose(new SceneVisualBrief(
            3,
            "Langkah 1",
            "Unduh dan Install Ollama",
            SceneVisualLayout.Window,
            SceneVisualPalette.Slate,
            [],
            Note: "Menginstal Ollama",
            Command: "curl -fsSL https://ollama.com/install.sh | sh",
            Progress: 0.78));

        Assert.Contains("Terminal", svg);
        Assert.Contains("curl -fsSL https://ollama.com/install.sh | sh", svg);
        Assert.Contains("78%", svg);
        Assert.Contains("Menginstal Ollama", svg);
    }

    [Fact]
    public void Compose_ChatRendersBubblesAndOfflineStatus()
    {
        var svg = SceneVisualSvg.Compose(new SceneVisualBrief(
            5,
            "Langkah 3",
            "Chat Pertama",
            SceneVisualLayout.Chat,
            SceneVisualPalette.Sunset,
            [
                new SceneVisualCard("Halo! Bisa bantu bikin caption?"),
                new SceneVisualCard("Tentu, aku jalan offline di laptopmu.")
            ],
            Note: "AI Lokal"));

        Assert.Contains("Halo! Bisa bantu bikin", svg);
        Assert.Contains("caption?", svg);
        Assert.Contains("Tentu, aku jalan offline di", svg);
        Assert.Contains("laptopmu.", svg);
        Assert.Contains("offline", svg);
    }

    [Fact]
    public void Compose_EscapesMarkupCharacters()
    {
        var svg = SceneVisualSvg.Compose(new SceneVisualBrief(
            6,
            "Tips & Trik",
            "Aman <offline> & cepat",
            SceneVisualLayout.Cards,
            SceneVisualPalette.Ocean,
            [new SceneVisualCard("A & B")]));

        Assert.Contains("Aman &lt;offline&gt; &amp; cepat", svg);
        Assert.Contains("A &amp; B", svg);
        Assert.DoesNotContain("<offline>", svg);
    }

    [Theory]
    [InlineData(SceneVisualLayout.Hero)]
    [InlineData(SceneVisualLayout.Cards)]
    [InlineData(SceneVisualLayout.Window)]
    [InlineData(SceneVisualLayout.Chat)]
    public void Compose_EveryLayoutIsWellFormed(SceneVisualLayout layout)
    {
        var svg = SceneVisualSvg.Compose(new SceneVisualBrief(
            1,
            "Kicker",
            "Heading",
            layout,
            SceneVisualPalette.Ocean,
            [new SceneVisualCard("Satu", "Dua", SceneVisualIcon.Check)],
            Note: "Catatan"));

        Assert.StartsWith("<svg", svg);
        Assert.EndsWith("</svg>", svg);
        Assert.Equal(1, CountOccurrences(svg, "<svg"));
        Assert.Equal(1, CountOccurrences(svg, "</svg>"));
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
