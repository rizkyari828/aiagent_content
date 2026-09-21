using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIStudio.Application.AI;
using AIStudio.Application.Concepts;
using AIStudio.Application.Creative;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Stories;

namespace AIStudio.StoryValidation;

/// <summary>
/// One validation case: the upstream CreativeDirection artifact directory and the
/// caller-selected registered NarrativePattern. Qwen does not choose the pattern in
/// v1, so the harness selects the closest existing pattern per case.
/// </summary>
public sealed record ValidationCaseSetup(string DirectoryName, string Name, string PatternId);

/// <summary>
/// The five real-Qwen CreativeDirection cases and their existing registered
/// patterns. Only the two seeded patterns exist; tech-explainer maps to the
/// explanatory flow (hook/context/explanation/example/takeaway) and the four
/// narrative/commercial cases map to problem-solution-short
/// (hook/problem/discovery/solution/payoff).
/// </summary>
public static class CaseCatalog
{
    public static IReadOnlyList<ValidationCaseSetup> Cases { get; } =
    [
        new("case-1-tech-explainer", "tech-explainer", "explanatory-flow"),
        new("case-2-anime-story", "anime-story", "problem-solution-short"),
        new("case-3-preschool-3d-story", "preschool-3d-story", "problem-solution-short"),
        new("case-4-photoreal-product-short", "photoreal-product-short", "problem-solution-short"),
        new("case-5-pet-mascot-commercial", "pet-mascot-commercial", "problem-solution-short")
    ];
}

/// <summary>Compact per-beat summary for human narrative review.</summary>
public sealed record BeatSummary
{
    public int Order { get; init; }

    public string Role { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    public int TargetDurationSeconds { get; init; }

    public IReadOnlyList<string> ContinuityFrom { get; init; } = [];
}

/// <summary>All validation dimensions for one case; written verbatim to artifacts.</summary>
public sealed record CaseReport
{
    public string Case { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string ExpectedPatternId { get; init; } = string.Empty;

    public string PatternId { get; init; } = string.Empty;

    public int PatternVersion { get; init; }

    public string PatternStatus { get; init; } = "unavailable";

    public bool JsonValid { get; init; }

    public bool SchemaValid { get; init; }

    public bool ConceptPreserved { get; init; }

    public string ExpectedConceptId { get; init; } = string.Empty;

    public string SourceConceptId { get; init; } = string.Empty;

    public int BeatCount { get; init; }

    public bool BeatOrderValid { get; init; }

    public int DurationTargetSeconds { get; init; }

    public int DurationSumSeconds { get; init; }

    public bool DurationValid { get; init; }

    public bool ContinuityValid { get; init; }

    public bool CharacterWorldRefsValid { get; init; }

    public IReadOnlyList<string> ScriptBoundaryHits { get; init; } = [];

    public IReadOnlyList<string> StoryboardBoundaryHits { get; init; } = [];

    public IReadOnlyList<string> ImplementationLeaks { get; init; } = [];

    public int OutputChars { get; init; }

    public int? OutputTokens { get; init; }

    public long LatencyMs { get; init; }

    public double? ProviderDurationMs { get; init; }

    public string Narrative { get; init; } = "unavailable";

    public string Result { get; init; } = "FAIL";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<BeatSummary> Beats { get; init; } = [];
}

/// <summary>Run-level metadata and totals.</summary>
public sealed record RunSummary
{
    public string StartedAtUtc { get; init; } = string.Empty;

    public string FinishedAtUtc { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; }

    public string DirectionsDir { get; init; } = string.Empty;

    public int Pass { get; init; }

    public int Review { get; init; }

    public int Fail { get; init; }

    public string Overall { get; init; } = string.Empty;

    public IReadOnlyList<CaseReport> Cases { get; init; } = [];
}

/// <summary>
/// Observer around the real production <see cref="IAiTextGenerator"/> so the harness
/// can preserve the exact prompt and raw model response. The director and provider
/// remain the production implementations.
/// </summary>
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
/// Flags obvious script-level output (final dialogue/narration) for human review.
/// A simple deterministic heuristic only; human review stays authoritative.
/// </summary>
public static class ScriptBoundaryScanner
{
    private static readonly Regex QuotedSpeech = new(
        "[\"\u201c][^\"\u201d]{4,}[\"\u201d]",
        RegexOptions.Compiled);

    private static readonly Regex SpeakerPrefix = new(
        @"(^|[\s.;])[A-Z][A-Za-z'-]{1,20}:\s+[A-Z""\u201c]",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> Scan(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var hits = new List<string>();

        if (QuotedSpeech.IsMatch(text))
        {
            hits.Add("quoted-speech");
        }

        if (SpeakerPrefix.IsMatch(text))
        {
            hits.Add("speaker-prefix");
        }

        return hits;
    }
}

/// <summary>
/// Flags obvious shot/camera instructions for human review. High-level visual
/// narrative language ("the student notices the screen") is intentionally allowed.
/// </summary>
public static class StoryboardBoundaryScanner
{
    private static readonly (string Label, Regex Pattern)[] Rules =
    [
        ("camera", new Regex(@"\bcamera\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("lens", new Regex(@"\b(35mm|50mm|85mm|lens|focal length|f/\d+(\.\d+)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("dolly", new Regex(@"\bdolly\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("rack-focus", new Regex(@"rack focus", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("pan", new Regex(@"\bpan (left|right)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("zoom", new Regex(@"\bzoom( in| out)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("shot-detail", new Regex(
            @"\b(shot list|shot number|frame number|establishing shot|wide shot|close[- ]up|storyboard)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("cut-to", new Regex(@"\bcut to\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))
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

/// <summary>
/// Flags forbidden runtime/provider/path details. Generic creative references to
/// animation, lighting, or audio are not leakage.
/// </summary>
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

/// <summary>
/// Deterministic RESULT policy: objective contract failures are FAIL, borderline
/// script/storyboard territory is REVIEW, otherwise PASS.
/// </summary>
public static class ResultPolicy
{
    public static string Compute(
        bool jsonValid,
        bool schemaValid,
        bool conceptPreserved,
        bool patternKnown,
        bool implementationLeak,
        bool scriptBoundary,
        bool storyboardBoundary)
    {
        if (!jsonValid || !schemaValid || !conceptPreserved || !patternKnown || implementationLeak)
        {
            return "FAIL";
        }

        return scriptBoundary || storyboardBoundary ? "REVIEW" : "PASS";
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
        Console.WriteLine($"  Concept       {FlagOrUnavailable(report.ConceptPreserved, report.SchemaValid)}");
        Console.WriteLine($"  Pattern       {PatternText(report)}");
        Console.WriteLine($"  Beats         {(report.SchemaValid ? $"{report.BeatCount} / PASS" : "unavailable")}");
        Console.WriteLine(
            $"  Duration      {(report.SchemaValid
                ? $"{report.DurationSumSeconds}s / target {report.DurationTargetSeconds}s / PASS"
                : "unavailable")}");
        Console.WriteLine($"  Continuity    {FlagOrUnavailable(report.ContinuityValid, report.SchemaValid)}");
        Console.WriteLine($"  Refs          {(report.SchemaValid ? "valid" : "unavailable")}");
        Console.WriteLine($"  Script        {BoundaryText(report.ScriptBoundaryHits)}");
        Console.WriteLine($"  Storyboard    {BoundaryText(report.StoryboardBoundaryHits)}");
        Console.WriteLine(
            $"  ImplLeak      {(report.ImplementationLeaks.Count == 0
                ? "none"
                : $"FAIL ({string.Join(", ", report.ImplementationLeaks)})")}");
        Console.WriteLine(
            $"  Output        {report.OutputChars} chars"
            + (report.OutputTokens is { } tokens ? $" / {tokens} tokens" : string.Empty));
        Console.WriteLine($"  Latency       {report.LatencyMs / 1000.0:0.0}s");
        Console.WriteLine($"  Narrative     {report.Narrative}");

        foreach (var beat in report.Beats)
        {
            var continuity = beat.ContinuityFrom.Count == 0
                ? "-"
                : string.Join(", ", beat.ContinuityFrom);

            Console.WriteLine(
                $"  Beat {beat.Order,-2} {beat.Role,-12} {beat.TargetDurationSeconds,4}s  "
                + $"continuity: {continuity,-10}  {Shorten(beat.Purpose, 80)}");
        }

        Console.WriteLine($"  RESULT        {report.Result}");

        if (!string.IsNullOrEmpty(report.ErrorMessage))
        {
            Console.WriteLine($"  Error         {report.ErrorCode}: {report.ErrorMessage}");
        }
    }

    private static string FlagOrUnavailable(bool value, bool schemaValid) =>
        schemaValid ? (value ? "PASS" : "FAIL") : "unavailable";

    private static string BoundaryText(IReadOnlyList<string> hits) =>
        hits.Count == 0 ? "none" : $"REVIEW ({string.Join(", ", hits)})";

    private static string PatternText(CaseReport report)
    {
        if (!report.SchemaValid)
        {
            return "unavailable";
        }

        var status = report.PatternStatus == "Known" ? "PASS" : report.PatternStatus;
        return $"{report.PatternId} v{report.PatternVersion} / {status}";
    }
}

/// <summary>
/// Deterministic, framework-free self-check of the harness logic. It reuses the
/// production StoryPlanParser/StoryPlanValidator and never calls a model.
/// </summary>
public static class SelfCheck
{
    private static readonly NarrativePatternRegistry Patterns = new(SeedNarrativePatterns.All);
    private static readonly NarrativePatternId SamplePattern = new("problem-solution-short");

    public static int Run()
    {
        var checks = new (string Description, Func<bool> Run)[]
        {
            ("recognizes a valid story plan", () =>
                StoryPlanParser.Parse(JsonSerializer.Serialize(SamplePlan(), ValidationArtifacts.Json), Patterns).Beats.Count == 5),
            ("rejects malformed json", () => Throws(() => StoryPlanParser.Parse("{ not json", Patterns), StoryPlanParserErrorCodes.InvalidJson)),
            ("rejects invalid duration", () => Throws(
                () => StoryPlanParser.Parse(
                    JsonSerializer.Serialize(SamplePlan(beats: DurationShiftedBeats()), ValidationArtifacts.Json),
                    Patterns),
                StoryPlanParserErrorCodes.PlanInvalid)),
            ("rejects a continuity cycle", () => Throws(
                () => StoryPlanParser.Parse(
                    JsonSerializer.Serialize(SamplePlan(beats: CyclicBeats()), ValidationArtifacts.Json),
                    Patterns),
                StoryPlanParserErrorCodes.PlanInvalid)),
            ("catches obvious script dialogue", () =>
                ScriptBoundaryScanner.Scan("The student whispers, \"Do I know you?\"").Count > 0),
            ("accepts narrative intent without quoting dialogue", () =>
                ScriptBoundaryScanner.Scan("The student realizes the assistant remembers a conversation that never happened.").Count == 0),
            ("reads a serialized CreativeDirection artifact", CreativeDirectionRoundTrips),
            ("catches provider/url leakage", () =>
                ImplementationLeakScanner.Scan("Use Ollama at http://127.0.0.1:11434").Count > 0),
            ("accepts clean narrative prose", () =>
                ImplementationLeakScanner.Scan("The student notices the screen while the room becomes quiet.").Count == 0),
            ("serializes every artifact shape", SerializationWorks),
            ("maps PASS, REVIEW, and FAIL", () =>
                ResultPolicy.Compute(true, true, true, true, false, false, false) == "PASS"
                && ResultPolicy.Compute(true, true, true, true, false, false, true) == "REVIEW"
                && ResultPolicy.Compute(true, false, true, true, false, false, false) == "FAIL"
                && ResultPolicy.Compute(true, true, true, true, true, false, false) == "FAIL"
                && ResultPolicy.Compute(true, true, false, true, false, false, false) == "FAIL"
                && ResultPolicy.Compute(true, true, true, false, false, false, false) == "FAIL")
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
        catch (StoryPlanException exception)
        {
            return exception.Code == expectedCode;
        }
    }

    private static bool SerializationWorks()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new object[]
                {
                    SamplePlan(),
                    new CaseReport { Name = "case", Narrative = "REVIEW", Beats = [new BeatSummary { Order = 1, Role = "hook" }] },
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

    private static bool CreativeDirectionRoundTrips()
    {
        var direction = new CreativeDirection
        {
            IdeaReference = "case-1",
            Concept = new ConceptManifest
            {
                Id = new ConceptId("sample-concept"),
                Version = new ConceptVersion(1),
                Title = "Title",
                Description = "Description",
                Audience = "developers",
                Format = "youtube-short",
                Style = "clean-tech",
                RecipeId = new ProductionRecipeId("tech-explainer"),
                RecipeVersion = new ProductionRecipeVersion(1),
                Duration = 45,
                Tags = ["local-ai"]
            },
            Treatment = new CreativeTreatment
            {
                StoryApproach = "approach",
                HookTreatment = "hook",
                Pacing = "pacing",
                VisualStrategy = "visual",
                EndingTreatment = "ending"
            }
        };

        var json = JsonSerializer.Serialize(direction, ValidationArtifacts.Json);
        var parsed = CreativeDirectionParser.Parse(json);

        return parsed.Concept.Id.Value == "sample-concept" && parsed.Concept.Duration == 45;
    }

    private static StoryPlan SamplePlan(IReadOnlyList<StoryBeat>? beats = null) =>
        new()
        {
            Id = new StoryPlanId("sample-story"),
            Version = new StoryPlanVersion(1),
            SourceConceptId = new ConceptId("sample-concept"),
            NarrativePattern = new NarrativePatternId("problem-solution-short"),
            NarrativePatternVersion = new NarrativePatternVersion(1),
            TargetDurationSeconds = 100,
            Beats = beats ?? ValidBeats()
        };

    private static IReadOnlyList<StoryBeat> ValidBeats() =>
    [
        Beat("beat-01", 1, "hook", 15, []),
        Beat("beat-02", 2, "problem", 20, ["beat-01"]),
        Beat("beat-03", 3, "discovery", 25, ["beat-02"]),
        Beat("beat-04", 4, "solution", 25, ["beat-03"]),
        Beat("beat-05", 5, "payoff", 15, ["beat-04"])
    ];

    private static IReadOnlyList<StoryBeat> DurationShiftedBeats() =>
    [
        Beat("beat-01", 1, "hook", 5, []),
        Beat("beat-02", 2, "problem", 5, ["beat-01"]),
        Beat("beat-03", 3, "discovery", 5, ["beat-02"]),
        Beat("beat-04", 4, "solution", 5, ["beat-03"]),
        Beat("beat-05", 5, "payoff", 5, ["beat-04"])
    ];

    private static IReadOnlyList<StoryBeat> CyclicBeats() =>
    [
        Beat("beat-01", 1, "hook", 15, ["beat-05"]),
        Beat("beat-02", 2, "problem", 20, ["beat-01"]),
        Beat("beat-03", 3, "discovery", 25, ["beat-02"]),
        Beat("beat-04", 4, "solution", 25, ["beat-03"]),
        Beat("beat-05", 5, "payoff", 15, ["beat-04"])
    ];

    private static StoryBeat Beat(
        string id,
        int order,
        string role,
        int duration,
        IReadOnlyList<string> continuityFrom) =>
        new()
        {
            Id = new StoryBeatId(id),
            Order = order,
            Role = new StoryBeatRole(role),
            Importance = StoryBeat.DefaultImportance,
            Purpose = $"Narrative intent for the {role} beat.",
            TargetDurationSeconds = duration,
            ContinuityFrom = continuityFrom.Select(value => new StoryBeatId(value)).ToList()
        };
}
