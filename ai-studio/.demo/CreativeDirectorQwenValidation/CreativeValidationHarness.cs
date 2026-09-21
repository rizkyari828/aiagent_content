using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIStudio.Application.AI;
using AIStudio.Application.Concepts;
using AIStudio.Application.Creative;
using AIStudio.Application.ProductionRecipes;

namespace AIStudio.CreativeValidation;

/// <summary>One ApprovedIdea under validation plus a short console label.</summary>
public sealed record ValidationCase(string Name, ApprovedIdea Idea);

/// <summary>
/// The five representative approved ideas for real-Qwen validation. They stay
/// descriptive data only: no per-format C# branch exists anywhere in the director
/// or this harness, so a new content format never needs a new type.
/// </summary>
public static class ValidationCases
{
    public static IReadOnlyList<ValidationCase> All { get; } =
    [
        new(
            "tech-explainer",
            new ApprovedIdea
            {
                IdeaReference = "case-1-tech-explainer",
                Topic = "Running useful AI locally without expensive cloud APIs",
                Angle = "What a normal developer can realistically run on consumer hardware",
                Audience = "developers interested in local AI",
                Objective = "Show developers a practical local AI setup they can actually run",
                HookPremise = "You do not need a data center to run useful AI"
            }),
        new(
            "anime-story",
            new ApprovedIdea
            {
                IdeaReference = "case-2-anime-story",
                Topic = "A student discovers an AI assistant that remembers a conversation that never happened",
                Angle = "short mysterious reveal",
                Audience = "young adult short-form viewers",
                HookPremise = "The assistant answers a question the student never asked today",
                Constraints = new CreativeConstraints { PreferredStyle = "anime-cinematic" }
            }),
        new(
            "preschool-3d-story",
            new ApprovedIdea
            {
                IdeaReference = "case-3-preschool-3d-story",
                Topic = "A child learns to put toys back after playing",
                Angle = "simple playful cause-and-effect story",
                Audience = "preschool children and their parents",
                HookPremise = "One toy left out starts a gentle chain of playful consequences",
                Constraints = new CreativeConstraints { PreferredStyle = "bright-stylized-3d" }
            }),
        new(
            "photoreal-product-short",
            new ApprovedIdea
            {
                IdeaReference = "case-4-photoreal-product-short",
                Topic = "A compact desk lamp for people working in small rooms",
                Angle = "problem to product usefulness to compact payoff",
                Audience = "young workers and students",
                HookPremise = "A cramped desk gets one honest lighting upgrade",
                Constraints = new CreativeConstraints { PreferredStyle = "photoreal-lifestyle" }
            }),
        new(
            "pet-mascot-commercial",
            new ApprovedIdea
            {
                IdeaReference = "case-5-pet-mascot-commercial",
                Topic = "Cat food promoted through a recurring playful cat character",
                Angle = "hungry cat to product to satisfying payoff",
                Audience = "pet owners",
                HookPremise = "A hungry cat stages a small, funny protest until dinner appears",
                Constraints = new CreativeConstraints { PreferredStyle = "photoreal-pet-commercial" }
            })
    ];
}

/// <summary>
/// Wraps the real <see cref="IAiTextGenerator"/> to capture the exact prompt and
/// raw model response the Creative Director used. It is an observer only: the
/// director and provider are the production implementations, so there is no
/// parallel creative pipeline.
/// </summary>
public sealed class RecordingAiTextGenerator(IAiTextGenerator inner) : IAiTextGenerator
{
    public AiTextRequest? LastRequest { get; private set; }

    public AiTextResponse? LastResponse { get; private set; }

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

/// <summary>Compact, inspectable copy of the treatment fields for human review.</summary>
public sealed record TreatmentArtifact
{
    public string StoryApproach { get; init; } = string.Empty;

    public string HookTreatment { get; init; } = string.Empty;

    public string Pacing { get; init; } = string.Empty;

    public string VisualStrategy { get; init; } = string.Empty;

    public string EndingTreatment { get; init; } = string.Empty;

    public string? Tone { get; init; }

    public string? TransitionStrategy { get; init; }
}

/// <summary>All dimensions recorded for one case; written to artifacts verbatim.</summary>
public sealed record CaseReport
{
    public string Case { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool JsonValid { get; init; }

    public bool SchemaValid { get; init; }

    public bool IdeaPreserved { get; init; }

    public double IdeaCoverage { get; init; }

    public IReadOnlyList<string> IdeaMissingTokens { get; init; } = [];

    public bool AudiencePreserved { get; init; }

    public string? AudienceFromModel { get; init; }

    public bool AudienceReplacedByModel { get; init; }

    public string RecipeId { get; init; } = string.Empty;

    public string RecipeVersion { get; init; } = string.Empty;

    public string RecipeStatus { get; init; } = "Unknown";

    public IReadOnlyList<string> MissingCapabilities { get; init; } = [];

    public string Format { get; init; } = string.Empty;

    public string Style { get; init; } = string.Empty;

    public string TreatmentUsefulness { get; init; } = "N/A";

    public TreatmentArtifact? Treatment { get; init; }

    public IReadOnlyList<string> ImplementationLeaks { get; init; } = [];

    public int OutputChars { get; init; }

    public int? OutputTokens { get; init; }

    public long LatencyMs { get; init; }

    public double? ProviderDurationMs { get; init; }

    public string Result { get; init; } = "FAIL";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>Run-level metadata and totals, written next to the per-case artifacts.</summary>
public sealed record RunSummary
{
    public string StartedAtUtc { get; init; } = string.Empty;

    public string FinishedAtUtc { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; }

    public CreativePlanningContext PlanningContext { get; init; } = new();

    public int Pass { get; init; }

    public int Review { get; init; }

    public int Fail { get; init; }

    public string Overall { get; init; } = string.Empty;

    public IReadOnlyList<CaseReport> Cases { get; init; } = [];
}

/// <summary>Deterministic idea-preservation heuristic result.</summary>
public sealed record IdeaPreservation(
    double Coverage,
    bool Preserved,
    IReadOnlyList<string> Matched,
    IReadOnlyList<string> Missing);

/// <summary>Recipe existence + resolution summary for one direction.</summary>
public sealed record RecipeCheck(
    string Id,
    string Version,
    string Status,
    IReadOnlyList<string> MissingCapabilities);

/// <summary>
/// Deterministic token-overlap heuristic for "the original idea survived". It is a
/// signal for human review, not an artistic judgement: the model may express the
/// angle creatively, but the approved topic must stay recognizable.
/// </summary>
public static class IdeaTextMatcher
{
    private const double MinimumCoverage = 0.34;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "the", "for", "with", "that", "this", "from", "into",
        "over", "under", "through", "without", "about", "what", "when", "which",
        "while", "your", "their", "they", "them", "then", "than", "its", "are",
        "was", "were", "has", "have", "had", "how", "why", "who", "will", "would",
        "can", "could", "should", "does", "did", "doing", "done", "using", "use",
        "not", "but", "out", "via", "per", "onto", "upon", "some", "more", "most",
        "very", "also", "our", "you", "all", "any", "each", "other", "such",
        "only", "own", "same", "too", "just", "now", "one", "two", "get", "gets"
    };

    public static IdeaPreservation Check(string approvedText, string directionText)
    {
        var ideaTokens = Tokenize(approvedText, minimumLength: 4)
            .Where(token => !StopWords.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var directionWords = Tokenize(directionText, minimumLength: 3)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var matched = new List<string>();
        var missing = new List<string>();

        foreach (var token in ideaTokens)
        {
            if (directionWords.Any(word => IsRelated(token, word)))
            {
                matched.Add(token);
            }
            else
            {
                missing.Add(token);
            }
        }

        var coverage = ideaTokens.Count == 0
            ? 1d
            : (double)matched.Count / ideaTokens.Count;

        return new IdeaPreservation(
            coverage,
            coverage >= MinimumCoverage,
            matched,
            missing);
    }

    private static bool IsRelated(string first, string second)
    {
        var shorter = first.Length <= second.Length ? first : second;
        var longer = first.Length <= second.Length ? second : first;

        // Prefix match tolerates simple inflections (running/run, apis/api).
        return shorter.Length >= 3 && longer.StartsWith(shorter, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Tokenize(string text, int minimumLength) =>
        Regex.Split(text ?? string.Empty, "[^A-Za-z0-9]+")
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length >= minimumLength);
}

/// <summary>
/// Flags model output that leaks implementation detail the prompt forbids:
/// provider/model names, URLs, executable paths, shell fragments, or credentials.
/// A hit means the creative text is not clean data and needs human review.
/// </summary>
public static class ImplementationLeakScanner
{
    private static readonly (string Label, Regex Pattern)[] Rules =
    [
        ("url", new Regex(@"https?://", RegexOptions.IgnoreCase)),
        ("file-path", new Regex(
            @"\.(py|json|sh|exe|dll|cs|js|ts|yaml|yml|wav|mp4|png|jpe?g|txt|srt|bat|ps1|onnx|safetensors|gguf)\b",
            RegexOptions.IgnoreCase)),
        ("absolute-path", new Regex(
            @"(^|[\s""'(])~[/\\]|/home/|\b[A-Za-z]:\\",
            RegexOptions.IgnoreCase)),
        ("provider-or-model", new Regex(
            @"\b(ollama|qwen\w*|voxcpm\w*|ace[-\s]?step|comfyui|manim|ffmpeg|blender|flux|sdxl|stable[-\s]?diffusion|whisper|deepseek|openai|anthropic|gemini|mistral|llama\d*)\b",
            RegexOptions.IgnoreCase)),
        ("shell-or-exec", new Regex(
            @"`|\$\{?[A-Za-z_]|&&|\|\||\b(rm|curl|wget|bash|powershell|chmod|sudo)\b\s",
            RegexOptions.IgnoreCase)),
        ("credential", new Regex(
            @"\bapi[_-]?key\b|\bpassword\b|\bsecret\b|\bbearer\s+[A-Za-z0-9._-]+",
            RegexOptions.IgnoreCase))
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
/// Maps a model-selected recipe id to a known/unknown recipe and its current
/// resolution. Hallucinated ids and capability gaps stay visible instead of being
/// silently repaired.
/// </summary>
public static class RecipeLookup
{
    public static RecipeCheck Classify(
        CreativeDirection direction,
        IProductionRecipeRegistry registry,
        IProductionRecipeResolver resolver)
    {
        var idText = direction.Concept.RecipeId.Value;

        if (!ProductionRecipeId.IsValid(idText))
        {
            return new RecipeCheck(string.Empty, string.Empty, "Unknown", []);
        }

        var id = new ProductionRecipeId(idText);
        var declared = direction.Concept.RecipeVersion;
        ProductionRecipe? recipe = null;

        if (declared is { IsValid: true } version && registry.TryGet(id, version, out var exact))
        {
            recipe = exact;
        }
        else
        {
            registry.TryGetLatest(id, out recipe);
        }

        if (recipe is null)
        {
            return new RecipeCheck(
                idText,
                declared?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                "Unknown",
                []);
        }

        var resolution = resolver.Resolve(recipe);
        var missing = resolution.Gaps
            .Select(gap => gap.CapabilityId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

        return new RecipeCheck(
            recipe.Id.Value,
            recipe.Version.Value.ToString(CultureInfo.InvariantCulture),
            StatusName(resolution.Status),
            missing);
    }

    public static string StatusName(ProductionRecipeStatus status) => status switch
    {
        ProductionRecipeStatus.FullySupported => "FullySupported",
        ProductionRecipeStatus.SupportedWithFallbacks => "SupportedWithFallbacks",
        ProductionRecipeStatus.Unsupported => "Unsupported",
        _ => "Unknown"
    };
}

/// <summary>
/// Deterministic RESULT policy. Hard director failures are FAIL; a hallucinated
/// recipe is REVIEW; a known recipe that is currently unsupported is reported but
/// stays PASS because a capability gap is not a Creative Director failure.
/// </summary>
public static class ResultPolicy
{
    public static string Compute(
        bool jsonValid,
        bool schemaValid,
        bool ideaPreserved,
        bool audiencePreserved,
        bool implementationLeak,
        string recipeStatus)
    {
        if (!jsonValid || !schemaValid || !ideaPreserved || !audiencePreserved || implementationLeak)
        {
            return "FAIL";
        }

        return string.Equals(recipeStatus, "Unknown", StringComparison.Ordinal)
            ? "REVIEW"
            : "PASS";
    }
}

/// <summary>Shared artifact JSON formatting for every preserved validation file.</summary>
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
            using var document = System.Text.Json.JsonDocument.Parse(text);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    public static string DirectionText(CreativeDirection direction) => string.Join(
        ' ',
        direction.Concept.Title,
        direction.Concept.Description,
        direction.Concept.Format,
        direction.Concept.Style,
        string.Join(' ', direction.Concept.Tags),
        direction.Treatment.StoryApproach,
        direction.Treatment.HookTreatment,
        direction.Treatment.Pacing,
        direction.Treatment.VisualStrategy,
        direction.Treatment.EndingTreatment,
        direction.Treatment.Tone,
        direction.Treatment.TransitionStrategy);

    public static string Shorten(string? value, int maximum = 160)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var normalized = string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum] + "...";
    }

    public static void PrintReport(CaseReport report)
    {
        Console.WriteLine($"  JSON          {Status(report.JsonValid)}");
        Console.WriteLine($"  Schema        {Status(report.SchemaValid)}");
        Console.WriteLine($"  Idea          {Status(report.IdeaPreserved)} ({report.IdeaCoverage:P0} coverage)");
        Console.WriteLine(
            $"  Audience      {Status(report.AudiencePreserved)}"
            + (report.AudienceReplacedByModel
                ? $" (model audience '{Shorten(report.AudienceFromModel, 48)}' restored by guardrail)"
                : string.Empty));
        Console.WriteLine($"  Recipe        {RecipeText(report)}");
        Console.WriteLine($"  Format        {report.Format}");
        Console.WriteLine($"  Style         {report.Style}");
        Console.WriteLine(
            $"  Leak          {(report.ImplementationLeaks.Count == 0
                ? "none"
                : string.Join(", ", report.ImplementationLeaks))}");
        Console.WriteLine(
            $"  Output        {report.OutputChars} chars"
            + (report.OutputTokens is { } tokens ? $" / {tokens} tokens" : string.Empty));
        Console.WriteLine($"  Latency       {report.LatencyMs / 1000.0:0.0}s");
        Console.WriteLine($"  Treatment     {report.TreatmentUsefulness}");

        if (report.Treatment is { } treatment)
        {
            Console.WriteLine($"    storyApproach      {Shorten(treatment.StoryApproach)}");
            Console.WriteLine($"    hookTreatment      {Shorten(treatment.HookTreatment)}");
            Console.WriteLine($"    pacing             {Shorten(treatment.Pacing)}");
            Console.WriteLine($"    visualStrategy     {Shorten(treatment.VisualStrategy)}");
            Console.WriteLine($"    endingTreatment    {Shorten(treatment.EndingTreatment)}");
            Console.WriteLine($"    tone               {Shorten(treatment.Tone)}");
            Console.WriteLine($"    transitionStrategy {Shorten(treatment.TransitionStrategy)}");
        }

        Console.WriteLine($"  RESULT        {report.Result}");

        if (!string.IsNullOrEmpty(report.ErrorMessage))
        {
            Console.WriteLine($"  Error         {report.ErrorCode}: {report.ErrorMessage}");
        }
    }

    private static string Status(bool value) => value ? "PASS" : "FAIL";

    private static string RecipeText(CaseReport report)
    {
        if (string.IsNullOrEmpty(report.RecipeId))
        {
            return "none / Unknown";
        }

        var text = $"{report.RecipeId} / {report.RecipeStatus}";

        if (report.RecipeStatus == "Unsupported" && report.MissingCapabilities.Count > 0)
        {
            text += $" (missing: {string.Join(", ", report.MissingCapabilities)})";
        }

        return text;
    }
}

/// <summary>
/// One runnable, framework-free check of the harness' deterministic logic. It
/// never calls a model, so it fails fast if a heuristic regresses.
/// </summary>
public static class SelfCheck
{
    public static int Run()
    {
        var checks = new (string Description, Func<bool> Run)[]
        {
            ("preserves a real idea", () => IdeaTextMatcher.Check(
                "a compact desk lamp for small rooms",
                "The Compact Desk Lamp That Saves Small Rooms").Preserved),
            ("rejects a replaced idea", () => !IdeaTextMatcher.Check(
                "a compact desk lamp for small rooms",
                "A day in the life of a productivity app for busy founders").Preserved),
            ("tolerates simple inflections", () => IdeaTextMatcher.Check(
                "running useful ai locally",
                "Run useful AI locally without cloud costs").Preserved),
            ("detects a provider/url leak", () => ImplementationLeakScanner.Scan(
                "Use Ollama at http://127.0.0.1:11434 with qwen3.8.").Count > 0),
            ("accepts clean creative prose", () => ImplementationLeakScanner.Scan(
                "A warm, playful story about a hungry cat who finds a cozy meal.").Count == 0),
            ("maps a supported recipe to PASS", () =>
                ResultPolicy.Compute(true, true, true, true, false, "FullySupported") == "PASS"),
            ("maps a hallucinated recipe to REVIEW", () =>
                ResultPolicy.Compute(true, true, true, true, false, "Unknown") == "REVIEW"),
            ("maps a lost idea to FAIL", () =>
                ResultPolicy.Compute(true, true, false, true, false, "FullySupported") == "FAIL"),
            ("maps a leak to FAIL", () =>
                ResultPolicy.Compute(true, true, true, true, true, "FullySupported") == "FAIL"),
            ("names every recipe status", () =>
                RecipeLookup.StatusName(ProductionRecipeStatus.FullySupported) == "FullySupported"
                && RecipeLookup.StatusName(ProductionRecipeStatus.SupportedWithFallbacks) == "SupportedWithFallbacks"
                && RecipeLookup.StatusName(ProductionRecipeStatus.Unsupported) == "Unsupported"),
            ("serializes every artifact shape", SerializationWorks)
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

    private static bool SerializationWorks()
    {
        var direction = new CreativeDirection
        {
            IdeaReference = "case-1",
            Concept = new ConceptManifest
            {
                Id = new ConceptId("local-ai-tech-explainer"),
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
                EndingTreatment = "ending",
                Tone = "tone",
                TransitionStrategy = "transition"
            }
        };

        var summary = new RunSummary
        {
            Model = "model",
            Cases =
            [
                new CaseReport
                {
                    Name = "case",
                    Treatment = new TreatmentArtifact { StoryApproach = "approach" }
                }
            ]
        };

        try
        {
            var json = JsonSerializer.Serialize(
                new object[] { direction, summary },
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
