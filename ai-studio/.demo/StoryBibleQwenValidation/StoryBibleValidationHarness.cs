using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIStudio.Application.AI;
using AIStudio.Application.Bibles;
using AIStudio.Application.Concepts;
using AIStudio.Application.Stories;
using AIStudio.BibleSemanticValidation;

namespace AIStudio.StoryBibleValidation;

/// <summary>One Story Bible validation case: its artifact directory name and short label.</summary>
public sealed record ValidationCaseSetup(string DirectoryName, string Name);

/// <summary>
/// Only the two upstream cases this milestone validates. Additional formats are
/// intentionally out of scope until evidence justifies the extra model cost.
/// </summary>
public static class CaseCatalog
{
    public static IReadOnlyList<ValidationCaseSetup> Cases { get; } =
    [
        new("case-2-anime-story", "anime-story"),
        new("case-3-preschool-3d-story", "preschool-3d-story")
    ];
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

/// <summary>Flags forbidden engine/provider/path/runtime detail in a proposal or its raw response.</summary>
public static class ImplementationLeakScanner
{
    private static readonly (string Label, Regex Pattern)[] Rules =
    [
        ("url", new Regex(@"https?://", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("file-path", new Regex(
            @"\.(py|json|sh|exe|dll|cs|js|ts|yaml|yml|ckpt|safetensors|gguf|bin|onnx|png|jpe?g|webp|gif|psd|mp4|mov|wav|mp3)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("absolute-path", new Regex(
            @"(^|[\s""'(])~[/\\]|/home/|/usr/|/tmp/|/opt/|/models?/|/assets?/|\b[A-Za-z]:\\",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("provider-or-model", new Regex(
            @"\b(ollama|qwen\w*|voxcpm\w*|ace[-\s]?step|comfyui|manim|ffmpeg|blender|flux|sdxl|stable[-\s]?diffusion|whisper|deepseek|openai|anthropic|gemini|mistral|llama\d*|checkpoint)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("shell-or-exec", new Regex(
            @"`|\$\{?[A-Za-z_]|&&|\|\||-filter_complex|\b(rm|curl|wget|bash|powershell|chmod|sudo|pip|conda|docker)\b\s",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("credential", new Regex(
            @"\bapi[_-]?key\b|\bpassword\b|\bsecret\b|\bbearer\s+[A-Za-z0-9._-]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    public static IReadOnlyList<string> Scan(string? text)
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

/// <summary>A PASS/REVIEW signal plus the short evidence a human should review.</summary>
public sealed record SignalStatus
{
    public string Status { get; init; } = "unavailable";

    public IReadOnlyList<string> Details { get; init; } = [];
}

/// <summary>Stable-id reuse across beats for one bible kind.</summary>
public sealed record ReuseReport
{
    public string Status { get; init; } = "unavailable";

    /// <summary>How many distinct beats use the most-reused proposed id.</summary>
    public int MaximumBeatUse { get; init; }

    public IReadOnlyList<string> ReusedIds { get; init; } = [];
}

/// <summary>
/// Deterministic, evidence-only reuse signal. It never requires every beat to carry
/// references: a narrative may legitimately not need them. It answers only whether a
/// proposed id recurs across more than one beat.
/// </summary>
public static class ReuseAnalyzer
{
    public static ReuseReport Analyze(
        IReadOnlyList<string> proposedIds,
        IReadOnlyList<StoryBeatGrounding> groundings,
        bool character)
    {
        var useCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var grounding in groundings)
        {
            var refs = character ? grounding.CharacterRefs : grounding.WorldRefs;
            foreach (var reference in (refs ?? []).Distinct(StringComparer.Ordinal))
            {
                useCount[reference] = useCount.GetValueOrDefault(reference) + 1;
            }
        }

        if (proposedIds.Count == 0)
        {
            return new ReuseReport { Status = "REVIEW", MaximumBeatUse = 0, ReusedIds = [] };
        }

        var reused = useCount
            .Where(pair => pair.Value >= 2)
            .Select(pair => pair.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var maximum = useCount.Count == 0 ? 0 : useCount.Values.Max();

        return new ReuseReport
        {
            Status = maximum >= 2 ? "PASS" : "REVIEW",
            MaximumBeatUse = maximum,
            ReusedIds = reused
        };
    }
}

/// <summary>
/// Simple, non-semantic signal that several proposed ids may describe one recurring
/// identity. It never merges or edits anything: it only surfaces candidates for human
/// review when an id carries an obvious state-like segment or extends another id.
/// </summary>
public static class IdentityFragmentationScanner
{
    private static readonly Regex StateSegment = new(
        @"(^|-)(scared|afraid|terrified|fearful|happy|sad|angry|furious|calm|smiling|crying|running|final|young|old|dark|clean|messy|lit|bright|open|closed|worried|shocked|confused|relieved|excited)($|-)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> Scan(IEnumerable<string> proposedIds)
    {
        var ids = proposedIds.Distinct(StringComparer.Ordinal).ToList();
        var candidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in ids)
        {
            if (StateSegment.IsMatch(id))
            {
                candidates.Add(id);
            }

            if (ids.Any(other => other.Length > id.Length
                && other.StartsWith(id + "-", StringComparison.Ordinal)))
            {
                candidates.Add(id);
            }
        }

        return candidates.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    /// <summary>True when an id carries an obvious temporary-state token such as <c>-scared</c>.</summary>
    public static bool HasStateSegment(string? value) =>
        !string.IsNullOrEmpty(value) && StateSegment.IsMatch(value);
}

/// <summary>
/// Lexical signal that temporary scene state leaked into stable character identity.
/// It reports REVIEW-only evidence and never fails, edits, or reinterprets a bible.
/// </summary>
public static class IdentityQualityScanner
{
    private static readonly Regex StateWording = new(
        @"\b(currently|right now|at this moment|in this scene|tonight|today|terrified|afraid|frightened|furious|holding|clutching|smiling|crying|running|angry|worried|shocked|confused|relieved)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> Scan(CharacterBible bible)
    {
        var details = new List<string>();
        var identity = bible.Identity ?? new CharacterIdentity();

        Inspect(identity.VisualDescription, "visualDescription", details);
        foreach (var trait in identity.DistinguishingTraits ?? [])
        {
            Inspect(trait, "distinguishingTrait", details);
        }

        foreach (var variant in bible.Variants ?? [])
        {
            if (variant is not null && IdentityFragmentationScanner.HasStateSegment(variant.Id))
            {
                details.Add($"variant:{variant.Id}");
            }
        }

        return details;
    }

    private static void Inspect(string? value, string field, List<string> details)
    {
        if (!string.IsNullOrWhiteSpace(value) && StateWording.IsMatch(value))
        {
            details.Add($"{field}:{Shorten(value!)}");
        }
    }

    private static string Shorten(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 80 ? normalized : normalized[..80] + "...";
    }
}

/// <summary>
/// Lexical signal that temporary scene state leaked into stable world identity.
/// REVIEW-only, like <see cref="IdentityQualityScanner"/>.
/// </summary>
public static class WorldIdentityQualityScanner
{
    private static readonly Regex StateWording = new(
        @"\b(currently|right now|tonight|today|this evening|this morning|midnight|raining|stormy|snowing|dark because|lights? (are|is)|turned off|screen (is|was) off|weather|scattered|messy|piled|scattered)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> Scan(WorldBible bible)
    {
        var details = new List<string>();
        var identity = bible.Identity ?? new WorldIdentity();

        Inspect(identity.VisualDescription, "visualDescription", details);
        foreach (var trait in identity.SpatialTraits ?? [])
        {
            Inspect(trait, "spatialTrait", details);
        }

        foreach (var rule in bible.ContinuityRules ?? [])
        {
            Inspect(rule, "continuityRule", details);
        }

        return details;
    }

    private static void Inspect(string? value, string field, List<string> details)
    {
        if (!string.IsNullOrWhiteSpace(value) && StateWording.IsMatch(value))
        {
            details.Add($"{field}:{Shorten(value!)}");
        }
    }

    private static string Shorten(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 80 ? normalized : normalized[..80] + "...";
    }
}

/// <summary>
/// Asset-reference policy for v1: empty is expected and accepted; a fabricated
/// implementation asset is a hard failure; any other non-empty reference is surfaced
/// for human review but never required.
/// </summary>
public static class AssetReferencePolicy
{
    public static SignalStatus Evaluate(StoryBiblePlan plan, IReadOnlyList<string> implementationLeaks)
    {
        var total = plan.CharacterBibles.Sum(bible => bible.AssetReferences.Count)
            + plan.WorldBibles.Sum(bible => bible.AssetReferences.Count);

        if (implementationLeaks.Count > 0)
        {
            return new SignalStatus
            {
                Status = "FAIL",
                Details = implementationLeaks.Select(leak => $"fabricated:{leak}").ToList()
            };
        }

        if (total == 0)
        {
            return new SignalStatus { Status = "PASS", Details = ["empty"] };
        }

        return new SignalStatus
        {
            Status = "REVIEW",
            Details = [$"{total} non-empty reference(s) need human review"]
        };
    }
}

/// <summary>Granular grounding checks computed from the parsed proposal using production models.</summary>
public sealed record GroundingCheck
{
    public bool BeatGroundingsValid { get; init; }

    public bool RelationshipsValid { get; init; }

    public bool CharacterRefsResolve { get; init; }

    public bool WorldRefsResolve { get; init; }
}

/// <summary>Deterministic inspection of a validated proposal's ids, refs, and relationships.</summary>
public static class ProposalInspector
{
    public static GroundingCheck Inspect(StoryBiblePlan plan, StoryPlan storyPlan)
    {
        var planBeats = (storyPlan.Beats ?? [])
            .Select(beat => beat.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        var characterIds = plan.CharacterBibles.Select(bible => bible.Id.Value).ToHashSet(StringComparer.Ordinal);
        var worldIds = plan.WorldBibles.Select(bible => bible.Id.Value).ToHashSet(StringComparer.Ordinal);

        var beatsValid = true;
        var characterRefsResolve = true;
        var worldRefsResolve = true;

        foreach (var grounding in plan.BeatGroundings)
        {
            if (!planBeats.Contains(grounding.BeatId.Value))
            {
                beatsValid = false;
            }

            foreach (var reference in grounding.CharacterRefs ?? [])
            {
                if (!characterIds.Contains(reference))
                {
                    characterRefsResolve = false;
                }
            }

            foreach (var reference in grounding.WorldRefs ?? [])
            {
                if (!worldIds.Contains(reference))
                {
                    worldRefsResolve = false;
                }
            }
        }

        var relationshipsValid = plan.CharacterBibles.All(bible =>
            (bible.Relationships ?? []).All(relationship =>
                relationship is not null
                && relationship.Target != bible.Id
                && characterIds.Contains(relationship.Target.Value)));

        return new GroundingCheck
        {
            BeatGroundingsValid = beatsValid,
            RelationshipsValid = relationshipsValid,
            CharacterRefsResolve = characterRefsResolve,
            WorldRefsResolve = worldRefsResolve
        };
    }
}

/// <summary>
/// Checks that grounding changed only beat character/world refs and preserved every
/// other narrative property of the original plan. The original plan is never mutated.
/// </summary>
public static class StoryPlanImmutability
{
    public static IReadOnlyList<string> Compare(StoryPlan original, StoryPlan grounded)
    {
        var differences = new List<string>();

        if (original.Id != grounded.Id)
        {
            differences.Add("id");
        }

        if (original.Version != grounded.Version)
        {
            differences.Add("version");
        }

        if (original.SourceConceptId != grounded.SourceConceptId)
        {
            differences.Add("sourceConceptId");
        }

        if (original.NarrativePattern != grounded.NarrativePattern)
        {
            differences.Add("narrativePattern");
        }

        if (original.NarrativePatternVersion != grounded.NarrativePatternVersion)
        {
            differences.Add("narrativePatternVersion");
        }

        if (original.TargetDurationSeconds != grounded.TargetDurationSeconds)
        {
            differences.Add("targetDurationSeconds");
        }

        var originalBeats = original.Beats ?? [];
        var groundedBeats = grounded.Beats ?? [];

        if (originalBeats.Count != groundedBeats.Count)
        {
            differences.Add("beatCount");
            return differences;
        }

        for (var index = 0; index < originalBeats.Count; index++)
        {
            var left = originalBeats[index];
            var right = groundedBeats[index];

            if (left.Id != right.Id
                || left.Order != right.Order
                || left.Role != right.Role
                || !string.Equals(left.Purpose, right.Purpose, StringComparison.Ordinal)
                || !string.Equals(left.Importance, right.Importance, StringComparison.Ordinal)
                || left.TargetDurationSeconds != right.TargetDurationSeconds
                || !left.ContinuityFrom.SequenceEqual(right.ContinuityFrom))
            {
                differences.Add($"beat[{index}]");
            }
        }

        return differences;
    }
}

/// <summary>Deterministic RESULT policy. Identity concerns are REVIEW, contract failures are FAIL.</summary>
public static class ResultPolicy
{
    public static string Compute(bool objectiveValid, bool reviewSignals)
    {
        if (!objectiveValid)
        {
            return "FAIL";
        }

        return reviewSignals ? "REVIEW" : "PASS";
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

public sealed record CharacterPreview
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string? Species { get; init; }

    public string? AgePresentation { get; init; }

    public IReadOnlyList<string> PersonalityTraits { get; init; } = [];

    public string? VisualDescription { get; init; }
}

public sealed record WorldPreview
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string EnvironmentType { get; init; } = string.Empty;

    public IReadOnlyList<string> RecurringProps { get; init; } = [];

    public string VisualDescription { get; init; } = string.Empty;
}

public sealed record GroundingPreview
{
    public string BeatId { get; init; } = string.Empty;

    public IReadOnlyList<string> CharacterRefs { get; init; } = [];

    public IReadOnlyList<string> WorldRefs { get; init; } = [];
}

/// <summary>All validation dimensions for one case; written verbatim to artifacts.</summary>
public sealed record CaseReport
{
    public string Case { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool JsonValid { get; init; }

    public bool SchemaValid { get; init; }

    public int CharacterCount { get; init; }

    public int WorldCount { get; init; }

    public bool CharacterIdsValid { get; init; }

    public bool WorldIdsValid { get; init; }

    public bool CharacterIdsUnique { get; init; }

    public bool WorldIdsUnique { get; init; }

    public bool RelationshipsValid { get; init; }

    public bool BeatGroundingsValid { get; init; }

    public bool CharacterRefsResolve { get; init; }

    public bool WorldRefsResolve { get; init; }

    public ReuseReport CharacterReuse { get; init; } = new();

    public ReuseReport WorldReuse { get; init; } = new();

    public SignalStatus IdentityFragmentation { get; init; } = new();

    public SignalStatus IdentityQuality { get; init; } = new();

    public SignalStatus WorldIdentityQuality { get; init; } = new();

    /// <summary>Human-review signal for likely internal WorldBible field contradictions.</summary>
    public SignalStatus WorldIdentityCoherence { get; init; } = new();

    /// <summary>Human-review signal for a prop duplicated as a character bible.</summary>
    public SignalStatus EntityRoleCoherence { get; init; } = new();

    public SignalStatus AssetReferences { get; init; } = new();

    public IReadOnlyList<string> ImplementationLeaks { get; init; } = [];

    public SignalStatus GroundApply { get; init; } = new();

    public SignalStatus StoryContext { get; init; } = new();

    public SignalStatus Immutability { get; init; } = new();

    public int OutputChars { get; init; }

    public int? OutputTokens { get; init; }

    public long LatencyMs { get; init; }

    public double? ProviderDurationMs { get; init; }

    public string Result { get; init; } = "FAIL";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<CharacterPreview> Characters { get; init; } = [];

    public IReadOnlyList<WorldPreview> Worlds { get; init; } = [];

    public IReadOnlyList<GroundingPreview> Groundings { get; init; } = [];
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

    public static string StatusOf(bool value) => value ? "PASS" : "FAIL";

    public static SignalStatus ToStatus(BibleSignal signal) =>
        new() { Status = signal.Status, Details = signal.Details };

    public static string SignalText(SignalStatus signal) =>
        signal.Details.Count == 0
            ? signal.Status
            : $"{signal.Status} ({string.Join("; ", signal.Details)})";

    public static void PrintReport(CaseReport report)
    {
        Console.WriteLine($"  JSON                    {StatusOf(report.JsonValid)}");
        Console.WriteLine($"  Schema                  {StatusOf(report.SchemaValid)}");
        Console.WriteLine($"  Characters              {report.CharacterCount}");
        Console.WriteLine($"  Worlds                  {report.WorldCount}");
        Console.WriteLine($"  CharacterIds            {StatusOf(report.CharacterIdsValid)}");
        Console.WriteLine($"  WorldIds                {StatusOf(report.WorldIdsValid)}");
        Console.WriteLine($"  Relationships           {StatusOf(report.RelationshipsValid)}");
        Console.WriteLine($"  Groundings              {StatusOf(report.BeatGroundingsValid)}");
        Console.WriteLine($"  CharacterRefs           {StatusOf(report.CharacterRefsResolve)}");
        Console.WriteLine($"  WorldRefs               {StatusOf(report.WorldRefsResolve)}");
        Console.WriteLine($"  CharacterReuse          {ReuseText(report.CharacterReuse)}");
        Console.WriteLine($"  WorldReuse              {ReuseText(report.WorldReuse)}");
        Console.WriteLine($"  IdentityFragmentation   {SignalText(report.IdentityFragmentation)}");
        Console.WriteLine($"  IdentityQuality         {SignalText(report.IdentityQuality)}");
        Console.WriteLine($"  WorldIdentityQuality    {SignalText(report.WorldIdentityQuality)}");
        Console.WriteLine($"  WorldIdentityCoherence  {SignalText(report.WorldIdentityCoherence)}");
        Console.WriteLine($"  EntityRoleCoherence     {SignalText(report.EntityRoleCoherence)}");
        Console.WriteLine(
            $"  ImplLeak                {(report.ImplementationLeaks.Count == 0
                ? "none"
                : $"FAIL ({string.Join(", ", report.ImplementationLeaks)})")}");
        Console.WriteLine($"  AssetReferences         {SignalText(report.AssetReferences)}");
        Console.WriteLine($"  GroundApply             {SignalText(report.GroundApply)}");
        Console.WriteLine($"  StoryContext            {SignalText(report.StoryContext)}");
        Console.WriteLine($"  Immutability            {SignalText(report.Immutability)}");
        Console.WriteLine(
            $"  Output                  {report.OutputChars} chars"
            + (report.OutputTokens is { } tokens ? $" / {tokens} tokens" : string.Empty));
        Console.WriteLine($"  Latency                 {report.LatencyMs / 1000.0:0.0}s");
        Console.WriteLine($"  RESULT                  {report.Result}");

        if (!string.IsNullOrEmpty(report.ErrorMessage))
        {
            Console.WriteLine($"  Error                   {report.ErrorCode}: {report.ErrorMessage}");
        }

        if (report.Characters.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Characters:");
            foreach (var character in report.Characters)
            {
                Console.WriteLine($"    {character.Id}");
                Console.WriteLine($"      role: {character.Role}");
                Console.WriteLine($"      species: {character.Species ?? "-"}");
                Console.WriteLine($"      age: {character.AgePresentation ?? "-"}");
                Console.WriteLine(
                    $"      traits: {(character.PersonalityTraits.Count == 0 ? "-" : string.Join(", ", character.PersonalityTraits))}");
            }
        }

        if (report.Worlds.Count > 0)
        {
            Console.WriteLine("  Worlds:");
            foreach (var world in report.Worlds)
            {
                Console.WriteLine($"    {world.Id}");
                Console.WriteLine($"      environment: {world.EnvironmentType}");
                Console.WriteLine(
                    $"      props: {(world.RecurringProps.Count == 0 ? "-" : string.Join(", ", world.RecurringProps))}");
            }
        }

        if (report.Groundings.Count > 0)
        {
            Console.WriteLine("  Grounding:");
            foreach (var grounding in report.Groundings)
            {
                Console.WriteLine(
                    $"    {grounding.BeatId} -> chars=[{string.Join(", ", grounding.CharacterRefs)}] "
                    + $"worlds=[{string.Join(", ", grounding.WorldRefs)}]");
            }
        }
    }

    private static string ReuseText(ReuseReport report) =>
        report.ReusedIds.Count == 0
            ? $"{report.Status} (max beats {report.MaximumBeatUse})"
            : $"{report.Status} (max beats {report.MaximumBeatUse}: {string.Join(", ", report.ReusedIds)})";
}

/// <summary>
/// Framework-free self-check of the harness' deterministic logic. It reuses the
/// production parser, validators, grounder, and context builder, and never calls a
/// model.
/// </summary>
public static class SelfCheck
{
    private const string ValidProposal = """
        {
          "characterBibles": [
            {
              "id": "student-01",
              "version": 1,
              "displayName": "Student",
              "identity": {
                "role": "protagonist",
                "species": "human",
                "agePresentation": "young-adult",
                "bodyStyle": "slim",
                "hair": "short-black",
                "eyes": "brown",
                "visualDescription": "A calm student with short black hair.",
                "distinguishingTraits": ["red-hoodie"]
              },
              "personalityTraits": ["curious"],
              "baselineVariant": "default",
              "variants": [],
              "relationships": [],
              "assetReferences": []
            }
          ],
          "worldBibles": [
            {
              "id": "student-bedroom",
              "version": 1,
              "displayName": "Student Bedroom",
              "identity": {
                "environmentType": "bedroom",
                "visualDescription": "A small bedroom with a desk and a single window.",
                "spatialTraits": ["single-window"]
              },
              "recurringProps": ["desk"],
              "continuityRules": ["desk remains beside window"],
              "locations": [],
              "assetReferences": []
            }
          ],
          "beatGroundings": [
            { "beatId": "beat-01", "characterRefs": ["student-01"], "worldRefs": ["student-bedroom"] },
            { "beatId": "beat-02", "characterRefs": ["student-01"], "worldRefs": ["student-bedroom"] }
          ]
        }
        """;

    public static int Run()
    {
        var checks = new (string Description, Func<bool> Run)[]
        {
            ("recognizes a valid proposal", () =>
                StoryBiblePlanParser.Parse(ValidProposal, AnimePlan()).CharacterBibles.Count == 1),
            ("rejects malformed json", () => Throws(
                () => StoryBiblePlanParser.Parse("{ not json", AnimePlan()),
                StoryBiblePlanningErrorCodes.InvalidJson)),
            ("rejects a duplicate character id", () => Throws(
                () => StoryBiblePlanParser.Parse(DuplicateCharacter(), AnimePlan()),
                StoryBiblePlanningErrorCodes.DuplicateCharacter)),
            ("rejects a duplicate world id", () => Throws(
                () => StoryBiblePlanParser.Parse(DuplicateWorld(), AnimePlan()),
                StoryBiblePlanningErrorCodes.DuplicateWorld)),
            ("rejects an unknown beat grounding", () => Throws(
                () => StoryBiblePlanParser.Parse(UnknownBeat(), AnimePlan()),
                StoryBiblePlanningErrorCodes.BeatUnknown)),
            ("rejects an unknown character ref", () => Throws(
                () => StoryBiblePlanParser.Parse(UnknownCharacterRef(), AnimePlan()),
                StoryBiblePlanningErrorCodes.CharacterRefUnknown)),
            ("rejects an unknown world ref", () => Throws(
                () => StoryBiblePlanParser.Parse(UnknownWorldRef(), AnimePlan()),
                StoryBiblePlanningErrorCodes.WorldRefUnknown)),
            ("accepts a legitimate repeated id across beats", () =>
            {
                var plan = StoryBiblePlanParser.Parse(ValidProposal, AnimePlan());
                var reuse = ReuseAnalyzer.Analyze(
                    plan.CharacterBibles.Select(bible => bible.Id.Value).ToList(),
                    plan.BeatGroundings,
                    character: true);
                return reuse.Status == "PASS" && reuse.MaximumBeatUse == 2;
            }),
            ("detects obvious provider/path leakage", () =>
                ImplementationLeakScanner.Scan(
                    "Save /models/student.png to C:\\work and render with ComfyUI checkpoint over http://x").Count > 0),
            ("accepts empty asset references", () =>
            {
                var plan = StoryBiblePlanParser.Parse(ValidProposal, AnimePlan());
                return AssetReferencePolicy.Evaluate(plan, []).Status == "PASS";
            }),
            ("registers the proposal and grounds successfully", () =>
            {
                var plan = StoryBiblePlanParser.Parse(ValidProposal, AnimePlan());
                var grounded = Ground(plan, AnimePlan());
                return grounded.Beats[0].CharacterRefs.SequenceEqual(["student-01"]);
            }),
            ("resolves grounded refs through StoryContext", () =>
            {
                var plan = StoryBiblePlanParser.Parse(ValidProposal, AnimePlan());
                var original = AnimePlan();
                var grounded = Ground(plan, original);
                var result = Context(plan, original, grounded);
                return result.IsValid
                    && result.Context.Characters.Count == 1
                    && result.Context.Characters[0].Id.Value == "student-01"
                    && result.Context.Worlds.Count == 1;
            }),
            ("preserves original story plan narrative fields", () =>
            {
                var plan = StoryBiblePlanParser.Parse(ValidProposal, AnimePlan());
                var original = AnimePlan();
                var grounded = Ground(plan, original);
                return StoryPlanImmutability.Compare(original, grounded).Count == 0
                    && original.Beats[0].CharacterRefs.Count == 0;
            }),
            ("surfaces scene-state identity as REVIEW without mutating data", () =>
            {
                var bible = new CharacterBible
                {
                    Id = new CharacterBibleId("student-01"),
                    Version = new CharacterBibleVersion(1),
                    DisplayName = "Student",
                    Identity = new CharacterIdentity
                    {
                        Role = "protagonist",
                        VisualDescription = "currently terrified and holding the laptop"
                    }
                };
                var before = JsonSerializer.Serialize(bible, ValidationArtifacts.Json);
                var quality = IdentityQualityScanner.Scan(bible);
                var after = JsonSerializer.Serialize(bible, ValidationArtifacts.Json);
                return quality.Count > 0 && before == after;
            }),
            ("coherent world identity passes coherence review", () =>
                BibleSemanticSignals.WorldIdentityCoherence([World("study-room-01", "Study Room", "room")]).Status == "PASS"),
            ("unsupported world classification surfaces REVIEW while staying structurally valid", () =>
            {
                var world = World("playroom-01", "The Playroom", "bedroom");
                return BibleSemanticSignals.WorldIdentityCoherence([world]).Status == "REVIEW"
                    && WorldBibleValidator.Validate(world).Count == 0;
            }),
            ("passive prop duplicated as a character surfaces REVIEW", () =>
            {
                var character = Character("plushie-01", "supporting", "toy", "The Plushie");
                var world = World("playroom-01", "The Playroom", "playroom", ["red-block", "plushie", "ball"]);
                return BibleSemanticSignals.EntityRoleCoherence([character], [world]).Status == "REVIEW";
            }),
            ("autonomous non-human character is not rejected for environment association", () =>
            {
                var character = Character("ai-interface-01", "antagonist", "ai", "AI Interface");
                var world = World("digital-space-01", "Digital Space", "void", ["ai-interface"]);
                return BibleSemanticSignals.EntityRoleCoherence([character], [world]).Status == "PASS";
            }),
            ("coherence signals never change bible data", () =>
            {
                var character = Character("plushie-01", "supporting", "toy", "The Plushie");
                var world = World("playroom-01", "The Playroom", "bedroom", ["red-block", "plushie", "ball"]);
                var characterBefore = JsonSerializer.Serialize(character, ValidationArtifacts.Json);
                var worldBefore = JsonSerializer.Serialize(world, ValidationArtifacts.Json);
                BibleSemanticSignals.WorldIdentityCoherence([world]);
                BibleSemanticSignals.EntityRoleCoherence([character], [world]);
                return characterBefore == JsonSerializer.Serialize(character, ValidationArtifacts.Json)
                    && worldBefore == JsonSerializer.Serialize(world, ValidationArtifacts.Json);
            }),
            ("parser and domain validators remain unchanged", () =>
            {
                var world = World("playroom-01", "The Playroom", "bedroom", ["red-block", "plushie", "ball"]);
                var character = Character("plushie-01", "supporting", "toy", "The Plushie");
                return WorldBibleValidator.Validate(world).Count == 0
                    && CharacterBibleValidator.Validate(character).Count == 0
                    && StoryBiblePlanParser.Parse(ValidProposal, AnimePlan()).CharacterBibles.Count == 1;
            }),
            ("maps PASS, REVIEW, and FAIL", () =>
                ResultPolicy.Compute(true, false) == "PASS"
                && ResultPolicy.Compute(true, true) == "REVIEW"
                && ResultPolicy.Compute(false, false) == "FAIL"
                && ResultPolicy.Compute(false, true) == "FAIL")
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

    private static StoryPlan Ground(StoryBiblePlan plan, StoryPlan storyPlan) =>
        new StoryPlanGrounder(
            new CharacterBibleRegistry(plan.CharacterBibles),
            new WorldBibleRegistry(plan.WorldBibles))
        .Ground(new StoryPlanGroundingRequest { StoryPlan = storyPlan, Beats = plan.BeatGroundings });

    private static AIStudio.Application.StoryContext.StoryContextBuildResult Context(
        StoryBiblePlan plan,
        StoryPlan storyPlan,
        StoryPlan grounded) =>
        new AIStudio.Application.StoryContext.StoryContextBuilder(
            new CharacterBibleRegistry(plan.CharacterBibles),
            new WorldBibleRegistry(plan.WorldBibles))
        .Build(new AIStudio.Application.StoryContext.StoryContextRequest
        {
            StoryPlan = grounded
        });

    private static bool Throws(Action action, string expectedCode)
    {
        try
        {
            action();
            return false;
        }
        catch (StoryBiblePlanningException exception)
        {
            return exception.Code == expectedCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static WorldBible World(
        string id,
        string displayName,
        string environmentType,
        IReadOnlyList<string>? recurringProps = null) =>
        new()
        {
            Id = new WorldBibleId(id),
            Version = new WorldBibleVersion(1),
            DisplayName = displayName,
            Identity = new WorldIdentity
            {
                EnvironmentType = environmentType,
                VisualDescription = "A place."
            },
            RecurringProps = recurringProps ?? []
        };

    private static CharacterBible Character(string id, string role, string species, string displayName) =>
        new()
        {
            Id = new CharacterBibleId(id),
            Version = new CharacterBibleVersion(1),
            DisplayName = displayName,
            Identity = new CharacterIdentity
            {
                Role = role,
                Species = species,
                VisualDescription = "A character."
            }
        };

    /// <summary>Minimal two-beat plan used only by the deterministic self-check.</summary>
    private static StoryPlan AnimePlan() => new()
    {
        Id = new StoryPlanId("anime-test-story"),
        Version = new StoryPlanVersion(1),
        SourceConceptId = new ConceptId("anime-test"),
        NarrativePattern = new NarrativePatternId("problem-solution-short"),
        NarrativePatternVersion = new NarrativePatternVersion(1),
        TargetDurationSeconds = 10,
        Beats =
        [
            new StoryBeat
            {
                Id = new StoryBeatId("beat-01"),
                Order = 1,
                Role = new StoryBeatRole("hook"),
                Purpose = "The student types a query.",
                TargetDurationSeconds = 5
            },
            new StoryBeat
            {
                Id = new StoryBeatId("beat-02"),
                Order = 2,
                Role = new StoryBeatRole("problem"),
                Purpose = "The interface answers a question never asked.",
                TargetDurationSeconds = 5,
                ContinuityFrom = [new StoryBeatId("beat-01")]
            }
        ]
    };

    private static string DuplicateCharacter() =>
        ValidProposal.Replace(
            "    }\n  ],\n  \"worldBibles\"",
            "    },\n    {\n      \"id\": \"student-01\",\n      \"version\": 1,\n      \"displayName\": \"Student Copy\",\n      \"identity\": { \"role\": \"supporting\", \"visualDescription\": \"Copy.\" },\n      \"baselineVariant\": \"default\"\n    }\n  ],\n  \"worldBibles\"",
            StringComparison.Ordinal);

    private static string DuplicateWorld() =>
        ValidProposal.Replace(
            "    }\n  ],\n  \"beatGroundings\"",
            "    },\n    {\n      \"id\": \"student-bedroom\",\n      \"version\": 1,\n      \"displayName\": \"Bedroom Copy\",\n      \"identity\": { \"environmentType\": \"bedroom\", \"visualDescription\": \"Copy.\" }\n    }\n  ],\n  \"beatGroundings\"",
            StringComparison.Ordinal);

    private static string UnknownBeat() =>
        ValidProposal.Replace("\"beatId\": \"beat-02\"", "\"beatId\": \"beat-99\"", StringComparison.Ordinal);

    private static string UnknownCharacterRef() =>
        ValidProposal.Replace(
            "{ \"beatId\": \"beat-02\", \"characterRefs\": [\"student-01\"], \"worldRefs\": [\"student-bedroom\"] }",
            "{ \"beatId\": \"beat-02\", \"characterRefs\": [\"ghost-01\"], \"worldRefs\": [\"student-bedroom\"] }",
            StringComparison.Ordinal);

    private static string UnknownWorldRef() =>
        ValidProposal.Replace(
            "{ \"beatId\": \"beat-02\", \"characterRefs\": [\"student-01\"], \"worldRefs\": [\"student-bedroom\"] }",
            "{ \"beatId\": \"beat-02\", \"characterRefs\": [\"student-01\"], \"worldRefs\": [\"ghost-room\"] }",
            StringComparison.Ordinal);
}
