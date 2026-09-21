using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIStudio.Application.AI;
using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Scripts;
using AIStudio.Application.StoryContext;
using AIStudio.Domain.Scripts;

namespace AIStudio.StoryboardValidation;

/// <summary>One Storyboard validation case: its directory name and short label.</summary>
public sealed record ValidationCaseSetup(string DirectoryName, string Name);

/// <summary>The five validated concepts, reused because the upstream planning chain passed on exactly these inputs.</summary>
public static class CaseCatalog
{
    public static IReadOnlyList<ValidationCaseSetup> Cases { get; } =
    [
        new("case-1-tech-explainer", "tech-explainer"),
        new("case-2-anime-story", "anime-story"),
        new("case-3-preschool-3d-story", "preschool-3d-story"),
        new("case-4-photoreal-product-short", "photoreal-product-short"),
        new("case-5-pet-mascot-commercial", "pet-mascot-commercial")
    ];
}

/// <summary>Fake content-project reader: no database, deterministic per case.</summary>
public sealed class StaticContentProjectReader(ContentProjectSnapshot snapshot) : IContentProjectReader
{
    public Task<ContentProjectSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        Task.FromResult<ContentProjectSnapshot?>(snapshot);
}

/// <summary>Fake script repository returning one approved reviewed script; no database.</summary>
public sealed class StaticScriptReviewRepository(ReviewedScript script) : IScriptReviewRepository
{
    public Task<ReviewedScript?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        Task.FromResult<ReviewedScript?>(
            script.ContentProjectId == contentProjectId ? script : null);

    public void Add(ReviewedScript reviewedScript) =>
        throw new NotSupportedException("The validation harness never writes scripts.");

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(0);
}

/// <summary>Observer around the real AI provider so the harness can preserve prompt/raw response.</summary>
public sealed class RecordingAiTextGenerator(IAiTextGenerator inner) : IAiTextGenerator
{
    public AiTextRequest? LastRequest { get; private set; }

    public AiTextResponse? LastResponse { get; private set; }

    public void Reset()
    {
        LastRequest = null;
        LastResponse = null;
    }

    public async Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = await inner.GenerateAsync(request, cancellationToken);
        LastResponse = response;
        return response;
    }
}

/// <summary>Observer around the real StoryContextBuilder installed into the production handler.</summary>
public sealed class RecordingStoryContextBuilder(IStoryContextBuilder inner) : IStoryContextBuilder
{
    public StoryContextBuildResult? LastResult { get; private set; }

    public void Reset() => LastResult = null;

    public StoryContextBuildResult Build(StoryContextRequest request)
    {
        var result = inner.Build(request);
        LastResult = result;
        return result;
    }
}

/// <summary>Empty registries: the real StoryPlans carry no character/world refs yet.</summary>
public static class StoryContextFactory
{
    public static IStoryContextBuilder Create() =>
        new StoryContextBuilder(new CharacterBibleRegistry(), new WorldBibleRegistry());
}

/// <summary>
/// Simple evidence-oriented detector for the visual field being used as replacement
/// spoken Script/dialogue instead of visual planning. Camera/framing language is
/// valid here and is not flagged.
/// </summary>
public static class ScriptRewriteScanner
{
    private static readonly (string Label, Regex Pattern)[] Rules =
    [
        ("narrator-attribution", new Regex(
            @"\b(narrator|voiceover|voice[- ]over)\b[^.]{0,20}\b(says|said|speaks|reads)\b|(^|[\s.;])(narrator|voiceover|voice[- ]over)\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("dialogue-attribution", new Regex(
            @"\b(says|said|asks|asked|replies|replied|whispers|shouts)\b\s*[:,]?\s*[""\u201c]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("speaker-prefix", new Regex(
            @"(^|[\s.;])[A-Z][A-Za-z'-]{1,20}\s*:\s*[""\u201c]",
            RegexOptions.Compiled)),
        ("long-quoted-block", new Regex(
            "[\"\u201c][^\"\u201d]{60,}[\"\u201d]",
            RegexOptions.Compiled))
    ];

    public static IReadOnlyList<string> Scan(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return Rules
            .Where(rule => rule.Pattern.IsMatch(text))
            .Select(rule => rule.Label)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>Flags forbidden engine/provider/path/runtime details. Camera/framing language is allowed.</summary>
public static class ImplementationLeakScanner
{
    private static readonly (string Label, Regex Pattern)[] Rules =
    [
        ("url", new Regex(@"https?://", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("file-path", new Regex(
            @"\.(py|json|sh|exe|dll|cs|js|ts|yaml|yml|ckpt|safetensors|gguf|bin|onnx)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("absolute-path", new Regex(
            @"(^|[\s""'(])~[/\\]|/home/|/usr/|/tmp/|/opt/|\b[A-Za-z]:\\",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("provider-or-model", new Regex(
            @"\b(ollama|qwen\w*|voxcpm\w*|ace[-\s]?step|comfyui|manim|ffmpeg|blender|flux|sdxl|stable[-\s]?diffusion|whisper|deepseek|openai|anthropic|gemini|mistral|llama\d*|checkpoint)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("shell-or-exec", new Regex(
            @"`|\$\{?[A-Za-z_]|&&|\|\||-filter_complex|\b(rm|curl|wget|bash|powershell|chmod|sudo|pip|conda)\b\s",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("credential", new Regex(
            @"\bapi[_-]?key\b|\bpassword\b|\bsecret\b|\bbearer\s+[A-Za-z0-9._-]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    public static IReadOnlyList<string> Scan(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return Rules
            .Where(rule => rule.Pattern.IsMatch(text))
            .Select(rule => rule.Label)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>Deterministic RESULT policy. Scene↔beat alignment is a human-review dimension, never a hard rule.</summary>
public static class ResultPolicy
{
    public static string Compute(
        bool jsonValid,
        bool schemaValid,
        bool implementationLeak,
        int scriptRewriteHits)
    {
        if (!jsonValid || !schemaValid || implementationLeak || scriptRewriteHits >= 2)
        {
            return "FAIL";
        }

        return scriptRewriteHits == 1 ? "REVIEW" : "PASS";
    }
}

/// <summary>Shared artifact JSON formatting.</summary>
public static class ValidationArtifacts
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record BeatPreview
{
    public int Order { get; init; }

    public string Role { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;
}

public sealed record SectionPreview
{
    public int Index { get; init; }

    public string Heading { get; init; } = string.Empty;

    public string NarrationPreview { get; init; } = string.Empty;
}

public sealed record ScenePreview
{
    public int Index { get; init; }

    public string Heading { get; init; } = string.Empty;

    public string Visual { get; init; } = string.Empty;
}

/// <summary>All validation dimensions for one case; written verbatim to artifacts.</summary>
public sealed record CaseReport
{
    public string Case { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool JsonValid { get; init; }

    public bool SchemaValid { get; init; }

    public bool TitleValid { get; init; }

    public int BeatCount { get; init; }

    public int SectionCount { get; init; }

    public int SceneCount { get; init; }

    public IReadOnlyList<string> ScriptRewriteHits { get; init; } = [];

    public IReadOnlyList<string> ImplementationLeaks { get; init; } = [];

    public int OutputChars { get; init; }

    public int? OutputTokens { get; init; }

    public long LatencyMs { get; init; }

    public double? ProviderDurationMs { get; init; }

    public string StoryAlignment { get; init; } = "unavailable";

    public string ScriptAlignment { get; init; } = "unavailable";

    public string VisualQuality { get; init; } = "unavailable";

    public string Result { get; init; } = "FAIL";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<BeatPreview> Beats { get; init; } = [];

    public IReadOnlyList<SectionPreview> Sections { get; init; } = [];

    public IReadOnlyList<ScenePreview> Scenes { get; init; } = [];
}

/// <summary>Run-level metadata and totals.</summary>
public sealed record RunSummary
{
    public string StartedAtUtc { get; init; } = string.Empty;

    public string FinishedAtUtc { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; }

    public string CreativeDirectionDir { get; init; } = string.Empty;

    public string StoryPlanDir { get; init; } = string.Empty;

    public string ScriptDir { get; init; } = string.Empty;

    public bool CharacterWorldGroundingExercised { get; init; }

    public int Pass { get; init; }

    public int Review { get; init; }

    public int Fail { get; init; }

    public string Overall { get; init; } = string.Empty;

    public IReadOnlyList<CaseReport> Cases { get; init; } = [];
}

/// <summary>Pure formatting helpers for the concise console report.</summary>
public static class ValidationOutput
{
    public static bool TryParseJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Shorten(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximum ? normalized : normalized[..maximum] + "...";
    }

    public static void PrintReport(CaseReport report)
    {
        Console.WriteLine($"  JSON             {(report.JsonValid ? "PASS" : "FAIL")}");
        Console.WriteLine($"  Schema           {(report.SchemaValid ? "PASS" : "FAIL")}");
        Console.WriteLine($"  Title            {(report.SchemaValid ? (report.TitleValid ? "PASS" : "FAIL") : "unavailable")}");
        Console.WriteLine($"  Beats            {report.BeatCount}");
        Console.WriteLine($"  Sections         {report.SectionCount}");
        Console.WriteLine($"  Scenes           {report.SceneCount}");
        Console.WriteLine($"  ScriptRewrite    {BoundaryText(report.ScriptRewriteHits)}");
        Console.WriteLine(
            $"  ImplLeak         {(report.ImplementationLeaks.Count == 0
                ? "none"
                : $"FAIL ({string.Join(", ", report.ImplementationLeaks)})")}");
        Console.WriteLine(
            $"  Output           {report.OutputChars} chars"
            + (report.OutputTokens is { } tokens ? $" / {tokens} tokens" : string.Empty));
        Console.WriteLine($"  Latency          {report.LatencyMs / 1000.0:0.0}s");
        Console.WriteLine($"  StoryAlignment   {report.StoryAlignment}");
        Console.WriteLine($"  ScriptAlignment  {report.ScriptAlignment}");
        Console.WriteLine($"  VisualQuality    {report.VisualQuality}");

        if (report.Beats.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  StoryPlan beats:");
            foreach (var beat in report.Beats)
            {
                Console.WriteLine($"    {beat.Order}. {beat.Role} - {Shorten(beat.Purpose, 100)}");
            }
        }

        if (report.Sections.Count > 0)
        {
            Console.WriteLine("  Script sections:");
            foreach (var section in report.Sections)
            {
                Console.WriteLine($"    {section.Index}. \"{Shorten(section.Heading, 48)}\" - {Shorten(section.NarrationPreview, 100)}");
            }
        }

        if (report.Scenes.Count > 0)
        {
            Console.WriteLine("  Storyboard scenes:");
            foreach (var scene in report.Scenes)
            {
                Console.WriteLine($"    {scene.Index}. \"{Shorten(scene.Heading, 48)}\" - {Shorten(scene.Visual, 120)}");
            }
        }

        Console.WriteLine($"  RESULT           {report.Result}");

        if (!string.IsNullOrEmpty(report.ErrorMessage))
        {
            Console.WriteLine($"  Error            {report.ErrorCode}: {report.ErrorMessage}");
        }
    }

    private static string BoundaryText(IReadOnlyList<string> hits) =>
        hits.Count == 0 ? "none" : $"REVIEW ({string.Join(", ", hits)})";
}

/// <summary>
/// Framework-free self-check of the harness' deterministic logic. It reuses the
/// production deserializer and never calls a model.
/// </summary>
public static class SelfCheck
{
    private const string ValidResult = """
        {
          "title": "Local AI Storyboard",
          "scenes": [
            { "heading": "Why local AI", "visual": "Close-up of a laptop running a local model." },
            { "heading": "Run the workflow", "visual": "Wide shot of the job queue and result viewer." }
          ]
        }
        """;

    public static int Run()
    {
        var checks = new (string Description, Func<bool> Run)[]
        {
            ("recognizes a valid storyboard result", () =>
                GenerateStoryboardResult.Deserialize(ValidResult).Scenes.Count == 2),
            ("rejects malformed json", () => Throws(
                () => GenerateStoryboardResult.Deserialize("{ not json"),
                "generate_storyboard_invalid_result")),
            ("rejects a missing scene field", () => Throws(
                () => GenerateStoryboardResult.Deserialize("""{"title":"T","scenes":[{"heading":"H"}]}"""),
                "generate_storyboard_invalid_result")),
            ("rejects an empty scene collection", () => Throws(
                () => GenerateStoryboardResult.Deserialize("""{"title":"T","scenes":[]}"""),
                "generate_storyboard_invalid_result")),
            ("detects provider/model leakage", () =>
                ImplementationLeakScanner.Scan("Send this to FLUX and render with ComfyUI node 42.").Count > 0),
            ("detects filesystem/command leakage", () =>
                ImplementationLeakScanner.Scan("Run ffmpeg -filter_complex and read /home/tama/scene.py.").Count > 0),
            ("allows legitimate camera/framing language", () =>
                ImplementationLeakScanner.Scan("Close-up of the student, then a wide shot and a slow pan across the room.").Count == 0
                && ScriptRewriteScanner.Scan("Close-up of the student, then a wide shot and a slow pan across the room.").Count == 0),
            ("detects rewritten spoken dialogue", () =>
                ScriptRewriteScanner.Scan("Narrator says: \"It was never real, and the room went cold.\"").Count > 0),
            ("allows in-world displayed text", () =>
                ScriptRewriteScanner.Scan("The AI interface displays the phrase 'Running locally' on screen.").Count == 0),
            ("serializes every artifact shape", SerializationWorks),
            ("maps PASS, REVIEW, and FAIL", () =>
                ResultPolicy.Compute(true, true, false, 0) == "PASS"
                && ResultPolicy.Compute(true, true, false, 1) == "REVIEW"
                && ResultPolicy.Compute(true, true, true, 0) == "FAIL"
                && ResultPolicy.Compute(true, false, false, 0) == "FAIL"
                && ResultPolicy.Compute(false, true, false, 0) == "FAIL"
                && ResultPolicy.Compute(true, true, false, 2) == "FAIL"),
            ("does not fail on scene-count differences", () =>
                ResultPolicy.Compute(true, true, false, 0) == "PASS")
        };

        var failures = 0;

        foreach (var check in checks)
        {
            var passed = check.Run();
            failures += passed ? 0 : 1;
            Console.WriteLine($"  [{(passed ? "ok" : "FAILED")}] {check.Description}");
        }

        Console.WriteLine(failures == 0
            ? $"Self-check PASS ({checks.Length}/{checks.Length} deterministic checks)."
            : $"Self-check FAIL ({failures} of {checks.Length} checks failed).");

        return failures == 0 ? 0 : 1;
    }

    private static bool Throws(Action action, string expectedCode)
    {
        try
        {
            action();
            return false;
        }
        catch (AIStudio.Application.Jobs.JobExecutionException exception)
        {
            return exception.ErrorCode == expectedCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool SerializationWorks()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new object[]
                {
                    GenerateStoryboardResult.Deserialize(ValidResult),
                    new CaseReport
                    {
                        Name = "case",
                        Beats = [new BeatPreview { Order = 1, Role = "hook" }],
                        Sections = [new SectionPreview { Index = 1, Heading = "h" }],
                        Scenes = [new ScenePreview { Index = 1, Heading = "s" }]
                    },
                    new RunSummary { Model = "model", Cases = [new CaseReport { Name = "case" }] }
                },
                ValidationArtifacts.Json);

            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
