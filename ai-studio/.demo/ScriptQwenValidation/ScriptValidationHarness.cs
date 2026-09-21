using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIStudio.Application.AI;
using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.StoryContext;

namespace AIStudio.ScriptValidation;

/// <summary>One Script validation case: its directory name and short label.</summary>
public sealed record ValidationCaseSetup(string DirectoryName, string Name);

/// <summary>The five validated concepts, reused because the planning layers passed on exactly these inputs.</summary>
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

/// <summary>Beat-to-section alignment row for human review (index alignment, not semantic proof).</summary>
public sealed record BeatAlignment
{
    public int BeatOrder { get; init; }

    public string BeatRole { get; init; } = string.Empty;

    public string BeatPurpose { get; init; } = string.Empty;

    public int? SectionIndex { get; init; }

    public string? SectionHeading { get; init; }

    public string? NarrationPreview { get; init; }
}

/// <summary>All validation dimensions for one case; written verbatim to artifacts.</summary>
public sealed record CaseReport
{
    public string Case { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool JsonValid { get; init; }

    public bool SchemaValid { get; init; }

    public string ExpectedConceptId { get; init; } = string.Empty;

    public bool ConceptPreserved { get; init; }

    public double ConceptCoverage { get; init; }

    public string ExpectedAudience { get; init; } = string.Empty;

    public bool AudiencePreserved { get; init; }

    public double AudienceCoverage { get; init; }

    public int BeatCount { get; init; }

    public int SectionCount { get; init; }

    public bool SectionCountMatches { get; init; }

    public bool OpeningHookValid { get; init; }

    public bool ClosingValid { get; init; }

    public IReadOnlyList<string> ScriptBoundaryHits { get; init; } = [];

    public IReadOnlyList<string> StoryboardBoundaryHits { get; init; } = [];

    public IReadOnlyList<string> ImplementationLeaks { get; init; } = [];

    public int OutputChars { get; init; }

    public int? OutputTokens { get; init; }

    public long LatencyMs { get; init; }

    public double? ProviderDurationMs { get; init; }

    public string NarrativeFlow { get; init; } = "unavailable";

    public string SpokenQuality { get; init; } = "unavailable";

    public string Result { get; init; } = "FAIL";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string Title { get; init; } = string.Empty;

    public string OpeningHook { get; init; } = string.Empty;

    public string Closing { get; init; } = string.Empty;

    public IReadOnlyList<BeatAlignment> Alignment { get; init; } = [];
}

/// <summary>Run-level metadata and totals.</summary>
public sealed record RunSummary
{
    public string StartedAtUtc { get; init; } = string.Empty;

    public string FinishedAtUtc { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; }

    public string Language { get; init; } = string.Empty;

    public string CreativeDirectionDir { get; init; } = string.Empty;

    public string StoryPlanDir { get; init; } = string.Empty;

    public bool CharacterWorldGroundingExercised { get; init; }

    public int Pass { get; init; }

    public int Review { get; init; }

    public int Fail { get; init; }

    public string Overall { get; init; } = string.Empty;

    public IReadOnlyList<CaseReport> Cases { get; init; } = [];
}

/// <summary>Fake content-project reader: no database, deterministic per case.</summary>
public sealed class StaticContentProjectReader(ContentProjectSnapshot snapshot) : IContentProjectReader
{
    public Task<ContentProjectSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        Task.FromResult<ContentProjectSnapshot?>(snapshot);
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

/// <summary>
/// Observer around the real <see cref="IStoryContextBuilder"/> installed into the
/// production <c>GenerateScriptJobHandler</c>, so the harness preserves the exact
/// projected context without reconstructing it.
/// </summary>
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

/// <summary>Flags camera/shot/editing execution language; spoken narration is not affected.</summary>
public static class StoryboardBoundaryScanner
{
    private static readonly (string Label, Regex Pattern)[] Rules =
    [
        ("camera", new Regex(@"\bcamera\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("close-up", new Regex(@"\bclose[- ]up\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("wide-shot", new Regex(@"\bwide shot\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("medium-shot", new Regex(@"\bmedium shot\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("lens", new Regex(@"\b(35mm|50mm|85mm|lens|focal length|f/\d+(\.\d+)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("pan", new Regex(@"\bpan (left|right)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("tilt", new Regex(@"\btilt\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("dolly", new Regex(@"\bdolly\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("rack-focus", new Regex(@"rack focus", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("cut", new Regex(@"\b(cut to|hard cut|cross-dissolve)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("zoom", new Regex(@"\bzoom( in| out)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("shot-detail", new Regex(
            @"\b(frame number|shot number|shot list|establishing shot|camera coordinates)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    public static IReadOnlyList<string> Scan(string text) => ScannerSupport.ScanRules(text, Rules);
}

/// <summary>Flags non-spoken production instructions rather than spoken content.</summary>
public static class ScriptBoundaryScanner
{
    private static readonly (string Label, Regex Pattern)[] Rules =
    [
        ("storyboard", new Regex(@"\bstoryboard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("shot-list", new Regex(@"\bshot list\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("b-roll", new Regex(@"\bb[- ]roll\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("image-prompt", new Regex(@"\b(image|visual) prompt\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("generate-image", new Regex(@"\bgenerate (an? )?image\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("render", new Regex(@"\brender (this|the) (scene|clip|shot)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    public static IReadOnlyList<string> Scan(string text) => ScannerSupport.ScanRules(text, Rules);
}

/// <summary>Flags forbidden runtime/provider/path details.</summary>
public static class ImplementationLeakScanner
{
    private static readonly (string Label, Regex Pattern)[] Rules =
    [
        ("url", new Regex(@"https?://", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("file-path", new Regex(
            @"\.(py|json|sh|exe|dll|cs|js|ts|yaml|yml|wav|mp4|png|jpe?g|txt|srt|bat|ps1|onnx|safetensors|gguf)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("absolute-path", new Regex(
            @"(^|[\s""'(])~[/\\]|/home/|/usr/|/tmp/|\b[A-Za-z]:\\",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("provider-or-model", new Regex(
            @"\b(ollama|qwen\w*|voxcpm\w*|ace[-\s]?step|comfyui|manim|ffmpeg|blender|flux|sdxl|stable[-\s]?diffusion|whisper|deepseek|openai|anthropic|gemini|mistral|llama\d*)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("shell-or-exec", new Regex(
            @"`|\$\{?[A-Za-z_]|&&|\|\||\b(rm|curl|wget|bash|powershell|chmod|sudo)\b\s",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("credential", new Regex(
            @"\bapi[_-]?key\b|\bpassword\b|\bsecret\b|\bbearer\s+[A-Za-z0-9._-]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    public static IReadOnlyList<string> Scan(string text) => ScannerSupport.ScanRules(text, Rules);
}

/// <summary>
/// Deterministic token-overlap check for "the upstream concept/audience survived".
/// A signal for human review, not an artistic judgement.
/// </summary>
public static class TextOverlap
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "the", "for", "with", "that", "this", "from", "into",
        "over", "under", "through", "without", "about", "what", "when", "which",
        "while", "your", "their", "they", "them", "then", "than", "its", "are",
        "was", "were", "has", "have", "had", "how", "why", "who", "will", "would",
        "can", "could", "should", "does", "did", "doing", "done", "using", "use",
        "not", "but", "out", "via", "per", "onto", "upon", "some", "more", "most",
        "very", "also", "our", "you", "all", "any", "each", "other", "such",
        "only", "own", "same", "too", "just", "now", "one", "two", "get", "gets",
        "interested", "people", "learn", "learns", "using"
    };

    public static double Coverage(string source, string target)
    {
        var sourceTokens = Tokenize(source, minimumLength: 4)
            .Where(token => !StopWords.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (sourceTokens.Count == 0)
        {
            return 1d;
        }

        var targetWords = Tokenize(target, minimumLength: 3)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var matched = sourceTokens.Count(token =>
            targetWords.Any(word => IsRelated(token, word)));

        return (double)matched / sourceTokens.Count;
    }

    private static bool IsRelated(string first, string second)
    {
        var shorter = first.Length <= second.Length ? first : second;
        var longer = first.Length <= second.Length ? second : first;
        return shorter.Length >= 3 && longer.StartsWith(shorter, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Tokenize(string text, int minimumLength) =>
        Regex.Split(text ?? string.Empty, "[^A-Za-z0-9]+")
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length >= minimumLength);
}

/// <summary>Deterministic RESULT policy for Script validation.</summary>
public static class ResultPolicy
{
    public const double MinimumConceptCoverage = 0.34;

    public static string Compute(
        bool jsonValid,
        bool schemaValid,
        bool sectionCountMatches,
        bool conceptPreserved,
        bool implementationLeak,
        bool scriptBoundary,
        int storyboardBoundaryHits,
        bool audiencePreserved)
    {
        if (!jsonValid || !schemaValid || !sectionCountMatches || !conceptPreserved
            || implementationLeak || scriptBoundary || storyboardBoundaryHits >= 3)
        {
            return "FAIL";
        }

        return storyboardBoundaryHits > 0 || !audiencePreserved ? "REVIEW" : "PASS";
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
        Console.WriteLine($"  JSON          {(report.JsonValid ? "PASS" : "FAIL")}");
        Console.WriteLine($"  Schema        {(report.SchemaValid ? "PASS" : "FAIL")}");
        Console.WriteLine($"  Concept       {FlagOrUnavailable(report.ConceptPreserved, report.SchemaValid)} ({report.ConceptCoverage:P0} coverage)");
        Console.WriteLine(
            $"  Audience      {FlagOrUnavailable(report.AudiencePreserved, report.SchemaValid)} "
            + $"[{report.ExpectedAudience}] ({report.AudienceCoverage:P0} coverage)");
        Console.WriteLine(
            $"  Sections      {(report.SchemaValid
                ? $"{report.SectionCount} / {report.BeatCount} {(report.SectionCountMatches ? "PASS" : "FAIL")}"
                : "unavailable")}");
        Console.WriteLine($"  Hook          {FlagOrUnavailable(report.OpeningHookValid, report.SchemaValid)}");
        Console.WriteLine($"  Closing       {FlagOrUnavailable(report.ClosingValid, report.SchemaValid)}");
        Console.WriteLine($"  Script        {BoundaryText(report.ScriptBoundaryHits, "FAIL")}");
        Console.WriteLine($"  Storyboard    {BoundaryText(report.StoryboardBoundaryHits, "REVIEW")}");
        Console.WriteLine(
            $"  ImplLeak      {(report.ImplementationLeaks.Count == 0
                ? "none"
                : $"FAIL ({string.Join(", ", report.ImplementationLeaks)})")}");
        Console.WriteLine(
            $"  Output        {report.OutputChars} chars"
            + (report.OutputTokens is { } tokens ? $" / {tokens} tokens" : string.Empty));
        Console.WriteLine($"  Latency       {report.LatencyMs / 1000.0:0.0}s");
        Console.WriteLine($"  Narrative     {report.NarrativeFlow}");
        Console.WriteLine($"  SpokenQuality {report.SpokenQuality}");

        if (report.Alignment.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"  Title: {report.Title}{Environment.NewLine}  Hook:  {Shorten(report.OpeningHook, 120)}");

            foreach (var row in report.Alignment)
            {
                var target = row.SectionHeading is null
                    ? "(no section)"
                    : $"\"{Shorten(row.SectionHeading, 48)}\"";

                Console.WriteLine(
                    $"  Beat {row.BeatOrder} {row.BeatRole,-12} -> Section {row.SectionIndex?.ToString() ?? "-"} {target}");
                Console.WriteLine($"    {Shorten(row.NarrationPreview, 120)}");
            }

            Console.WriteLine($"  Closing: {Shorten(report.Closing, 120)}");
        }

        Console.WriteLine($"  RESULT        {report.Result}");

        if (!string.IsNullOrEmpty(report.ErrorMessage))
        {
            Console.WriteLine($"  Error         {report.ErrorCode}: {report.ErrorMessage}");
        }
    }

    private static string FlagOrUnavailable(bool value, bool schemaValid) =>
        schemaValid ? (value ? "PASS" : "FAIL") : "unavailable";

    private static string BoundaryText(IReadOnlyList<string> hits, string verdict) =>
        hits.Count == 0 ? "none" : $"{verdict} ({string.Join(", ", hits)})";
}

internal static class ScannerSupport
{
    public static IReadOnlyList<string> ScanRules(
        string text,
        IReadOnlyList<(string Label, Regex Pattern)> rules)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return rules
            .Where(rule => rule.Pattern.IsMatch(text))
            .Select(rule => rule.Label)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>
/// Framework-free self-check of the harness' deterministic logic. It reuses the
/// production deserializer and never calls a model.
/// </summary>
public static class SelfCheck
{
    private const string ValidResult = """
        {
          "title": "Local AI Tutorial",
          "openingHook": "Build privately on your own machine.",
          "sections": [
            { "heading": "Why local AI", "narration": "Local inference keeps the workflow under your control." },
            { "heading": "Run the workflow", "narration": "Configure the model, execute the task, and review the result." }
          ],
          "closing": "Try the workflow and verify its limits."
        }
        """;

    public static int Run()
    {
        var checks = new (string Description, Func<bool> Run)[]
        {
            ("recognizes a valid script result", () =>
                GenerateScriptResult.Deserialize(ValidResult).Sections.Count == 2),
            ("rejects malformed json", () => Throws(
                () => GenerateScriptResult.Deserialize("{ not json"),
                "generate_script_invalid_result")),
            ("rejects a missing required field", () => Throws(
                () => GenerateScriptResult.Deserialize("""{"title":"Only a title"}"""),
                "generate_script_invalid_result")),
            ("detects a section/beat count mismatch", () => !Matches(2, 3) && Matches(3, 3)),
            ("detects camera/shot instructions", () =>
                StoryboardBoundaryScanner.Scan("Cut to a close-up of the student's face.").Count > 0),
            ("detects provider/path leakage", () =>
                ImplementationLeakScanner.Scan("Render this with Blender at /home/tama/scene.py").Count > 0),
            ("detects non-spoken production instructions", () =>
                ScriptBoundaryScanner.Scan("Storyboard: open on a b-roll montage.").Count > 0),
            ("allows ordinary spoken narration", () =>
                StoryboardBoundaryScanner.Scan("You can run useful AI on the machine you already own.").Count == 0
                && ImplementationLeakScanner.Scan("You can run useful AI on the machine you already own.").Count == 0),
            ("allows quoted character dialogue", () =>
                ScriptBoundaryScanner.Scan("The student asks, \"Do I know you?\" The assistant answers softly.").Count == 0),
            ("keeps concept overlap signal", () =>
                TextOverlap.Coverage("local AI on consumer hardware", "Run local AI on consumer hardware today.") >= ResultPolicy.MinimumConceptCoverage),
            ("serializes every artifact shape", SerializationWorks),
            ("maps PASS, REVIEW, and FAIL", () =>
                ResultPolicy.Compute(true, true, true, true, false, false, 0, true) == "PASS"
                && ResultPolicy.Compute(true, true, true, true, false, false, 1, true) == "REVIEW"
                && ResultPolicy.Compute(true, true, true, true, false, false, 0, false) == "REVIEW"
                && ResultPolicy.Compute(true, true, false, true, false, false, 0, true) == "FAIL"
                && ResultPolicy.Compute(true, true, true, false, false, false, 0, true) == "FAIL"
                && ResultPolicy.Compute(true, true, true, true, true, false, 0, true) == "FAIL"
                && ResultPolicy.Compute(true, true, true, true, false, true, 0, true) == "FAIL"
                && ResultPolicy.Compute(true, true, true, true, false, false, 3, true) == "FAIL")
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

    private static bool Matches(int sectionCount, int beatCount) => sectionCount == beatCount;

    private static bool Throws(Action action, string expectedCode)
    {
        try
        {
            action();
            return false;
        }
        catch (JobExecutionException exception)
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
                    GenerateScriptResult.Deserialize(ValidResult),
                    new CaseReport { Name = "case", Alignment = [new BeatAlignment { BeatOrder = 1, BeatRole = "hook" }] },
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
