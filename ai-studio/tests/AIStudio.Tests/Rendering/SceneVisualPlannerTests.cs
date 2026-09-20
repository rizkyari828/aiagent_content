using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Rendering.Visuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class SceneVisualPlannerTests
{
    [Theory]
    [InlineData("Chat pertama", "Bubble percakapan dengan jawaban AI", SceneVisualLayout.Chat)]
    [InlineData("Install Ollama", "Terminal browser dan progress download", SceneVisualLayout.Window)]
    [InlineData("Yang dibutuhkan", "Checklist tiga panel berisi daftar", SceneVisualLayout.Cards)]
    [InlineData("Opening", "Laptop terbuka dengan layar menyala", SceneVisualLayout.Hero)]
    public void ClassifyLayout_UsesExplicitKeywords(
        string heading,
        string visual,
        SceneVisualLayout expected)
    {
        var layout = SceneVisualPlanner.ClassifyLayout(new StoryboardScene(heading, visual));

        Assert.Equal(expected, layout);
    }

    [Fact]
    public void PlanAll_RoutesIntentToEngineDeterministically()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.AnimatedStoryboard);

        var plans = SceneVisualPlanner.PlanAll(storyboard, enableAnimation: true);

        Assert.Equal(SceneVisualIntent.TechnicalFlow, plans[0].Direction!.Intent);
        Assert.Equal(SceneVisualEngine.ManimAnimation, plans[0].Engine);
        Assert.Equal(SceneAnimationTemplate.LocalAiFlow, plans[0].Template);
        Assert.False(plans[0].IsFallback);

        Assert.Equal(SceneVisualIntent.Conversation, plans[1].Direction!.Intent);
        Assert.Equal(SceneVisualEngine.AnimatedSvg, plans[1].Engine);
        Assert.Equal(SceneAnimationTemplate.None, plans[1].Template);
    }

    [Fact]
    public void PlanAll_FallsBackToAnimatedSvgWhenAnimationIsDisabled()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.AnimatedStoryboard);

        var plans = SceneVisualPlanner.PlanAll(storyboard, enableAnimation: false);

        Assert.All(plans, plan => Assert.Equal(SceneVisualEngine.AnimatedSvg, plan.Engine));
        Assert.True(plans[0].IsFallback);
        Assert.Equal(SceneVisualEngine.ManimAnimation, plans[0].IntendedEngine);
        Assert.Equal(SceneAnimationTemplate.None, plans[0].Template);
        Assert.Null(plans[0].Animation);
    }

    [Fact]
    public void PlanAll_PopulatesAnimationParametersForAnimatedScenes()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.AnimatedStoryboard);

        var plan = Assert.Single(
            SceneVisualPlanner.PlanAll(storyboard, enableAnimation: true),
            candidate => candidate.Brief.SceneIndex == 0);

        Assert.NotNull(plan.Animation);
        Assert.False(string.IsNullOrWhiteSpace(plan.Animation!.PrimaryText));
        Assert.False(string.IsNullOrWhiteSpace(plan.Animation.Kicker));
        Assert.Equal(SceneVisualPlanner.AnimationDurationSeconds, plan.Animation.DurationSeconds);
        Assert.Contains(plan.Animation.Palette, Enum.GetValues<SceneVisualPalette>());
    }

    [Fact]
    public void PlanAll_ProcessFlowDifferentiatesInstallFromPullAndBoundsProgress()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            """
            {
              "title": "Terminal",
              "scenes": [
                { "heading": "Langkah 1: Download dan Install Ollama", "visual": "Terminal install dengan progress bar download." },
                { "heading": "Langkah 2: Tarik Model AI Ringan", "visual": "Terminal pull model dengan progress bar unduh." }
              ]
            }
            """);

        var plans = SceneVisualPlanner.PlanAll(storyboard, [4.0, 4.0], enableAnimation: true);

        Assert.All(plans, plan =>
        {
            Assert.Equal(SceneAnimationTemplate.ProcessFlow, plan.Template);
            Assert.Equal(SceneVisualEngine.ManimAnimation, plan.Engine);
            Assert.NotNull(plan.Animation);
            Assert.False(string.IsNullOrWhiteSpace(plan.Animation!.Command));
            Assert.NotEmpty(plan.Animation.Steps ?? []);
            Assert.InRange(plan.Animation.ProgressTarget, 0.05, 1.0);
        });

        // Install and Pull stay visually related (same ProcessFlow template) but
        // must not be identical: command, step labels and progress differ by intent.
        Assert.NotEqual(plans[0].Animation!.Command, plans[1].Animation!.Command);
        Assert.NotEqual(
            string.Join(',', plans[0].Animation!.Steps!),
            string.Join(',', plans[1].Animation!.Steps!));
        Assert.NotEqual(plans[0].Animation!.ProgressTarget, plans[1].Animation!.ProgressTarget);
    }

    [Fact]
    public void PlanAll_CyclesPalettesAndKeepsSceneIndexes()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.AnimatedStoryboard);

        var plans = SceneVisualPlanner.PlanAll(storyboard, enableAnimation: false);

        Assert.Equal([0, 1], plans.Select(plan => plan.Brief.SceneIndex).ToArray());
        Assert.NotEqual(plans[0].Brief.Palette, plans[1].Brief.Palette);
    }

    [Theory]
    [InlineData(SceneAnimationTemplate.None, SceneVisualEngine.SvgStill)]
    [InlineData(SceneAnimationTemplate.LocalAiFlow, SceneVisualEngine.ManimAnimation)]
    [InlineData(SceneAnimationTemplate.ChatFlow, SceneVisualEngine.ManimAnimation)]
    public void Select_MapsTemplateToEngine(
        SceneAnimationTemplate template,
        SceneVisualEngine expected)
    {
        Assert.Equal(expected, SceneVisualEngineSelector.Select(template));
    }

    [Fact]
    public void Select_PrefersAiImageForStillsWhenEnabled()
    {
        Assert.Equal(
            SceneVisualEngine.AiImage,
            SceneVisualEngineSelector.Select(SceneAnimationTemplate.None, enableAiImages: true));
        // An animation template still wins over the still image engine.
        Assert.Equal(
            SceneVisualEngine.ManimAnimation,
            SceneVisualEngineSelector.Select(SceneAnimationTemplate.ChatFlow, enableAiImages: true));
    }

    [Fact]
    public void Select_PrefersThreeDOverAiImageButNotOverManim()
    {
        Assert.Equal(
            SceneVisualEngine.ThreeD,
            SceneVisualEngineSelector.Select(
                SceneAnimationTemplate.None,
                enableAiImages: true,
                threeDTemplate: SceneThreeDTemplate.LocalAiLaptop));
        Assert.Equal(
            SceneVisualEngine.ManimAnimation,
            SceneVisualEngineSelector.Select(
                SceneAnimationTemplate.LocalAiFlow,
                threeDTemplate: SceneThreeDTemplate.LocalAiLaptop));
    }

    [Fact]
    public void PlanAll_RoutesOpeningSceneToThreeDWhenEnabled()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.OpeningStoryboard);

        var enabled = SceneVisualPlanner.PlanAll(
            storyboard,
            enableAnimation: true,
            enableThreeD: true);
        Assert.Equal(SceneVisualEngine.ThreeD, enabled[0].Engine);
        Assert.Equal(SceneThreeDTemplate.LocalAiLaptop, enabled[0].ThreeDTemplate);
        Assert.False(enabled[0].IsFallback);
        Assert.Equal(SceneVisualIntent.Opening, enabled[0].Direction!.Intent);

        var disabled = SceneVisualPlanner.PlanAll(
            storyboard,
            enableAnimation: true,
            enableThreeD: false);
        Assert.Equal(SceneVisualEngine.ManimAnimation, disabled[0].Engine);
        Assert.True(disabled[0].IsFallback);
        Assert.Equal(SceneVisualEngine.ThreeD, disabled[0].IntendedEngine);
    }

    [Fact]
    public void PlanAll_OpeningFallsBackToAnimatedSvgWhenGpuEnginesAreDisabled()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.OpeningStoryboard);

        var plans = SceneVisualPlanner.PlanAll(
            storyboard,
            enableAnimation: false,
            enableThreeD: false);

        Assert.Equal(SceneVisualEngine.AnimatedSvg, plans[0].Engine);
        Assert.True(plans[0].IsFallback);
        Assert.Equal(SceneVisualEngine.ThreeD, plans[0].IntendedEngine);
    }

    [Fact]
    public void PlanAll_RoutesClosingSceneToAiImageWhenEnabled()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.ClosingStoryboard);

        var enabled = SceneVisualPlanner.PlanAll(
            storyboard,
            enableAnimation: true,
            enableAiImages: true);
        Assert.Equal(SceneVisualEngine.AiImage, enabled[0].Engine);
        Assert.False(enabled[0].IsFallback);
        Assert.Equal(SceneVisualIntent.Closing, enabled[0].Direction!.Intent);

        var disabled = SceneVisualPlanner.PlanAll(
            storyboard,
            enableAnimation: true,
            enableAiImages: false);
        Assert.Equal(SceneVisualEngine.AnimatedSvg, disabled[0].Engine);
        Assert.True(disabled[0].IsFallback);
        Assert.Equal(SceneVisualEngine.AiImage, disabled[0].IntendedEngine);
    }

    [Fact]
    public void PlanAll_MapsVideoOneScenesToStructuredLayouts()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(VideoOneStoryboard);

        var plans = SceneVisualPlanner.PlanAll(storyboard, enableAnimation: false);

        Assert.Equal(
            [
                SceneVisualLayout.Cards,
                SceneVisualLayout.Window,
                SceneVisualLayout.Cards,
                SceneVisualLayout.Chat,
                SceneVisualLayout.Cards,
                SceneVisualLayout.Hero
            ],
            plans.Select(plan => plan.Brief.Layout).ToArray());
    }

    [Fact]
    public void PlanAll_BuildsSemanticCardsWithoutLeakingVisualInstructions()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(VideoOneStoryboard);

        var plans = SceneVisualPlanner.PlanAll(storyboard, enableAnimation: false);

        Assert.Equal(
            ["Laptop", "Ruang Disk", "Koneksi", "Ollama"],
            plans[0].Brief.Cards.Select(card => card.Title).ToArray());
        Assert.Equal(
            ["llama3.1:8b", "llama3.1:4b"],
            plans[2].Brief.Cards.Select(card => card.Title).ToArray());
        Assert.Equal("ollama pull llama3.1:8b", plans[2].Brief.Note);
        Assert.Contains(
            plans[3].Brief.Cards,
            card => card.Title.StartsWith("Jelaskan ke aku", StringComparison.Ordinal));
        Assert.Equal("AI itu buat kamu", plans[5].Brief.Cards[0].Title);

        foreach (var plan in plans)
        {
            var svg = SceneVisualSvg.Compose(plan.Brief);
            Assert.DoesNotContain("Tiga panel horizontal", svg);
            Assert.DoesNotContain("Layar bersih", svg);
            Assert.DoesNotContain("Orang yang sama dari opening", svg);
            Assert.DoesNotContain("antarmuka chat rapi", svg);
            Assert.DoesNotContain("ikon laptop", svg);
        }
    }

    private const string VideoOneStoryboard = """
        {
          "title": "AI di Laptop Tanpa Internet: Panduan 5 Menit untuk Pemula",
          "scenes": [
            {
              "heading": "Yang Kamu Butuhkan (Cuma Ini)",
              "visual": "Layar bersih dengan tiga item checklist muncul satu per satu: (1) ikon laptop dengan label 'RAM 8 GB', (2) ikon kabel internet dengan label 'sekali download', (3) ikon hard disk dengan label '5 GB kosong'. Di bawahnya, ikon Python, terminal, dan environment setup dicoret. Logo Ollama muncul di tengah dengan teks 'se-simple buka browser'."
            },
            {
              "heading": "Langkah 1: Download dan Install Ollama",
              "visual": "Browser terbuka, address bar menampilkan 'ollama.com'. Kursor klik tombol download di pojok kanan atas. Progress bar install berjalan cepat. Ikon Ollama muncul di system tray (Windows) atau menu bar (Mac). Jendela terminal terbuka, teks 'ollama --version' diketik, lalu angka versi muncul di bawahnya. Centang hijau muncul di samping."
            },
            {
              "heading": "Langkah 2: Tarik Model AI Ringan",
              "visual": "Jendela terminal menampilkan teks 'ollama pull llama3.1:8b' diikuti Enter. Progress bar download berjalan dari 0 ke 4,7 GB. Ikon model AI kecil 'mendarat' ke dalam ikon laptop. Di bawahnya, alternatif muncul: 'ollama pull llama3.1:4b' dengan progress bar lebih pendek (2,5 GB) dan label 'RAM 8 GB? Pakai ini.'"
            },
            {
              "heading": "Langkah 3: Chat Pertama dengan AI Offline",
              "visual": "Terminal menampilkan 'ollama run llama3.1:8b', prompt chat muncul. Teks pertanyaan 'Jelaskan ke aku apa itu machine learning...' diketik, lalu jawaban AI muncul baris demi baris. Ikon Wi-Fi di pojok layar diklik dan berubah jadi 'OFF'. Pertanyaan kedua diketik, AI tetap menjawab tanpa loading. Teks '100% milik kamu' muncul dengan ikon gembok."
            },
            {
              "heading": "Tips Biar Makin Nyaman",
              "visual": "Tiga panel horizontal: (1) antarmuka chat rapi dari Open WebUI dengan sidebar model, (2) terminal menampilkan 'ollama list' dengan daftar model dan 'ollama pull' untuk model baru, (3) laptop dengan indikator suhu naik, jendela aplikasi lain ditutup satu per satu, dan model lebih kecil dipilih dari daftar."
            },
            {
              "heading": "Closing",
              "visual": "Orang yang sama dari opening hook menutup laptop dengan percaya diri, lalu membukanya lagi dan langsung mengetik percakapan AI. Ikon 'share' dan 'subscribe' muncul di sudut. Teks penutup: 'AI itu buat kamu, di laptop yang kamu punya sekarang.' Ikon GPU mahal di pojok dicoret untuk terakhir kalinya."
            }
          ]
        }
        """;
}
