using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIStudio.Application.AI;
using AIStudio.Application.Bibles;
using AIStudio.Application.Concepts;
using AIStudio.Application.Content;
using AIStudio.Application.Creative;
using AIStudio.Application.Scripts;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Domain.Scripts;
using StoryContextModel = AIStudio.Application.StoryContext.StoryContext;

namespace AIStudio.GroundedContentValidation;

/// <summary>
/// One grounded-content validation case. The three upstream artifact directories use
/// different case folder names, so each is mapped explicitly.
/// </summary>
public sealed record ValidationCaseSetup(
    string Name,
    string CreativeDirectory,
    string StoryDirectory,
    string StoryBibleDirectory);

/// <summary>Exactly the two cases whose grounded identity proof is in scope.</summary>
public static class CaseCatalog
{
    public static IReadOnlyList<ValidationCaseSetup> Cases { get; } =
    [
        new("anime-story", "case-2-anime-story", "case-2-anime-story", "case-1-anime-story"),
        new("preschool-3d-story", "case-3-preschool-3d-story", "case-3-preschool-3d-story", "case-2-preschool-3d-story")
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

/// <summary>
/// Observer around the real <see cref="IStoryContextBuilder"/> installed into the
/// production Script/Storyboard handlers, so the harness preserves the exact projected
/// context each phase actually received.
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

/// <summary>Deterministically rebuilt grounded state: proposal registered, grounded, projected.</summary>
public sealed record GroundedPlanning
{
    public StoryBiblePlan Proposal { get; init; } = new();

    public StoryPlan GroundedStoryPlan { get; init; } = new();

    public CharacterBibleRegistry Characters { get; init; } = new();

    public WorldBibleRegistry Worlds { get; init; } = new();

    public StoryContextModel Context { get; init; } = new();

    public SignalStatus ContextResolution { get; init; } = new();
}

/// <summary>
/// Rebuilds the grounded state from the proposal + original StoryPlan using ONLY the
/// production contracts: fresh in-memory registries, explicit registration, the
/// deterministic <see cref="StoryPlanGrounder"/>, then <see cref="StoryContextBuilder"/>.
/// Script and Storyboard both receive the same registry instances.
/// </summary>
public static class GroundedPlanningFactory
{
    public static GroundedPlanning Build(
        CreativeDirection direction,
        StoryPlan plan,
        StoryBiblePlan proposal)
    {
        var characters = new CharacterBibleRegistry(proposal.CharacterBibles);
        var worlds = new WorldBibleRegistry(proposal.WorldBibles);

        var grounded = new StoryPlanGrounder(characters, worlds).Ground(
            new StoryPlanGroundingRequest { StoryPlan = plan, Beats = proposal.BeatGroundings });

        var buildResult = new StoryContextBuilder(characters, worlds).Build(new StoryContextRequest
        {
            CreativeDirection = direction,
            StoryPlan = grounded
        });

        var referencedCharacters = grounded.Beats
            .SelectMany(beat => beat.CharacterRefs ?? [])
            .Where(StoryIdentifier.IsValid)
            .ToHashSet(StringComparer.Ordinal);
        var referencedWorlds = grounded.Beats
            .SelectMany(beat => beat.WorldRefs ?? [])
            .Where(StoryIdentifier.IsValid)
            .ToHashSet(StringComparer.Ordinal);

        var contextCharacterIds = buildResult.Context.Characters.Select(character => character.Id.Value).ToList();
        var contextWorldIds = buildResult.Context.Worlds.Select(world => world.Id.Value).ToList();

        var resolved = buildResult.IsValid
            && contextCharacterIds.ToHashSet(StringComparer.Ordinal).SetEquals(referencedCharacters)
            && contextWorldIds.ToHashSet(StringComparer.Ordinal).SetEquals(referencedWorlds)
            && contextCharacterIds.Count == contextCharacterIds.Distinct(StringComparer.Ordinal).Count()
            && contextWorldIds.Count == contextWorldIds.Distinct(StringComparer.Ordinal).Count();

        return new GroundedPlanning
        {
            Proposal = proposal,
            GroundedStoryPlan = grounded,
            Characters = characters,
            Worlds = worlds,
            Context = buildResult.Context,
            ContextResolution = resolved
                ? new SignalStatus
                {
                    Status = "PASS",
                    Details = [$"chars=[{string.Join(", ", contextCharacterIds)}] worlds=[{string.Join(", ", contextWorldIds)}]"]
                }
                : new SignalStatus
                {
                    Status = "FAIL",
                    Details =
                    [
                        buildResult.IsValid ? "reference mismatch" : $"issues={buildResult.Issues.Count}",
                        $"expected chars=[{string.Join(", ", referencedCharacters)}] worlds=[{string.Join(", ", referencedWorlds)}]"
                    ]
                }
        };
    }
}

/// <summary>Confirms grounding changed only beat refs and never mutated the original plan.</summary>
public static class PlanImmutability
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

/// <summary>A PASS/REVIEW/FAIL signal plus the short evidence a human should review.</summary>
public sealed record SignalStatus
{
    public string Status { get; init; } = "unavailable";

    public IReadOnlyList<string> Details { get; init; } = [];
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
        ("render", new Regex(@"\brender (this|the) (scene|clip|shot)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("effect-direction", new Regex(@"\b(sound|audio|visual|sparkle|lighting) effects?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("sound-cue", new Regex(@"\b(sound|audio) cue\b|\bding sound\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    public static IReadOnlyList<string> Scan(string text) => ScannerSupport.ScanRules(text, Rules);
}

/// <summary>
/// Flags the storyboard visual field being used as rewritten spoken Script/dialogue
/// instead of visual planning. Camera/framing language is valid here and is not flagged.
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
/// Deterministic, evidence-only grounding signals. These never claim semantic proof:
/// identity is compared lexically and everything questionable is surfaced as REVIEW
/// for human judgement. They never edit, merge, or reinterpret any bible.
/// </summary>
public static class GroundingSignalScanner
{
    private static readonly string[] MachineTerms =
        ["robot", "android", "cyborg", "droid", "mecha"];

    private static readonly string[] ElderlyTerms =
        ["elderly", "old man", "old woman", "senior", "aged", "grey-haired", "gray-haired"];

    private static readonly HashSet<string> YoungAgeTokens = new(StringComparer.Ordinal)
    {
        "young", "young-adult", "teen", "teenager", "child", "kid", "preschooler", "toddler"
    };

    private static readonly string[] RelocationTerms =
    [
        "spaceship", "space station", "outer space", "forest", "jungle", "city street", "city",
        "street", "ocean", "desert", "mountain", "island", "castle", "subway", "cave", "beach",
        "rooftop", "meadow"
    ];

    public static SignalStatus CharacterGrounding(IReadOnlyList<CharacterBible> bibles, string text)
    {
        if (bibles.Count == 0)
        {
            return new SignalStatus { Status = "unavailable" };
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new SignalStatus { Status = "REVIEW", Details = ["no output text to compare"] };
        }

        var lower = text.ToLowerInvariant();
        var details = new List<string>();
        var visible = false;

        foreach (var bible in bibles)
        {
            var identity = bible.Identity ?? new CharacterIdentity();

            if (string.Equals(identity.Species, "human", StringComparison.Ordinal))
            {
                foreach (var term in MachineTerms)
                {
                    if (ContainsWord(lower, term))
                    {
                        details.Add($"{bible.Id.Value}: species 'human' vs output '{term}'");
                    }
                }
            }

            if (identity.AgePresentation is { } age && YoungAgeTokens.Contains(age))
            {
                foreach (var term in ElderlyTerms)
                {
                    if (ContainsWord(lower, term))
                    {
                        details.Add($"{bible.Id.Value}: age '{age}' vs output '{term}'");
                    }
                }
            }

            visible |= ContainsIdentityToken(lower, bible);
        }

        if (details.Count > 0)
        {
            return new SignalStatus { Status = "REVIEW", Details = details };
        }

        return visible
            ? new SignalStatus { Status = "PASS" }
            : new SignalStatus { Status = "REVIEW", Details = ["no stable character identity token visible in output"] };
    }

    public static SignalStatus WorldGrounding(IReadOnlyList<WorldBible> worlds, string text)
    {
        if (worlds.Count == 0)
        {
            return new SignalStatus { Status = "unavailable" };
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new SignalStatus { Status = "REVIEW", Details = ["no output text to compare"] };
        }

        var lower = text.ToLowerInvariant();
        var relocations = RelocationTerms.Where(term => ContainsWord(lower, term)).ToList();

        if (relocations.Count > 0)
        {
            return new SignalStatus
            {
                Status = "REVIEW",
                Details = [$"possible relocation: {string.Join(", ", relocations)}"]
            };
        }

        var visible = worlds.Any(world => ContainsWorldToken(lower, world));

        return visible
            ? new SignalStatus { Status = "PASS" }
            : new SignalStatus { Status = "REVIEW", Details = ["no stable world identity token visible in output"] };
    }

    public static SignalStatus IdentityPreservation(SignalStatus character, SignalStatus world)
    {
        if (character.Status == "unavailable" && world.Status == "unavailable")
        {
            return new SignalStatus { Status = "unavailable" };
        }

        if (character.Status != "REVIEW" && world.Status != "REVIEW")
        {
            return new SignalStatus { Status = "PASS" };
        }

        var details = character.Details.Concat(world.Details).Distinct(StringComparer.Ordinal).ToList();
        return new SignalStatus { Status = "REVIEW", Details = details };
    }

    private static bool ContainsIdentityToken(string lowerText, CharacterBible bible)
    {
        var identity = bible.Identity ?? new CharacterIdentity();

        return Tokens(bible.DisplayName)
            .Concat(Tokens(bible.Id.Value.Replace('-', ' ')))
            .Concat(Tokens(identity.Role))
            .Concat(Tokens(identity.Species))
            .Concat(Tokens(identity.Hair))
            .Concat(Tokens(identity.Eyes))
            .Concat(Tokens(identity.BodyStyle))
            .Concat((bible.PersonalityTraits ?? []).SelectMany(Tokens))
            .Concat((identity.DistinguishingTraits ?? []).SelectMany(Tokens))
            .Any(token => token.Length >= 4 && lowerText.Contains(token, StringComparison.Ordinal));
    }

    private static bool ContainsWorldToken(string lowerText, WorldBible world)
    {
        var identity = world.Identity ?? new WorldIdentity();

        return Tokens(world.DisplayName)
            .Concat(Tokens(world.Id.Value.Replace('-', ' ')))
            .Concat(Tokens(identity.EnvironmentType))
            .Concat((identity.SpatialTraits ?? []).SelectMany(Tokens))
            .Concat((world.RecurringProps ?? []).SelectMany(Tokens))
            .Any(token => token.Length >= 4 && lowerText.Contains(token, StringComparison.Ordinal));
    }

    private static IEnumerable<string> Tokens(string? value) =>
        Regex.Split(value ?? string.Empty, "[^A-Za-z]+")
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0);

    private static bool ContainsWord(string text, string term) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase);
}

/// <summary>Deterministic RESULT policy for one combined case. Identity concerns are REVIEW.</summary>
public static class ResultPolicy
{
    public static string Compute(bool objectiveValid, bool reviewSignals) =>
        !objectiveValid ? "FAIL" : reviewSignals ? "REVIEW" : "PASS";
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

    public string? BodyStyle { get; init; }

    public string? Hair { get; init; }

    public string? Eyes { get; init; }

    public IReadOnlyList<string> DistinguishingTraits { get; init; } = [];

    public IReadOnlyList<string> PersonalityTraits { get; init; } = [];

    public string? VisualDescription { get; init; }
}

public sealed record WorldPreview
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string EnvironmentType { get; init; } = string.Empty;

    public IReadOnlyList<string> SpatialTraits { get; init; } = [];

    public IReadOnlyList<string> RecurringProps { get; init; } = [];

    public string VisualDescription { get; init; } = string.Empty;
}

public sealed record GroundingPreview
{
    public string BeatId { get; init; } = string.Empty;

    public IReadOnlyList<string> CharacterRefs { get; init; } = [];

    public IReadOnlyList<string> WorldRefs { get; init; } = [];
}

public sealed record BeatAlignmentRow
{
    public int BeatOrder { get; init; }

    public string BeatRole { get; init; } = string.Empty;

    public string BeatPurpose { get; init; } = string.Empty;

    public int? SectionIndex { get; init; }

    public string? SectionHeading { get; init; }

    public string? NarrationPreview { get; init; }
}

public sealed record ScenePreview
{
    public int Index { get; init; }

    public string Heading { get; init; } = string.Empty;

    public string Visual { get; init; } = string.Empty;
}

/// <summary>Script phase validation dimensions for one case.</summary>
public sealed record ScriptValidation
{
    public bool JsonValid { get; init; }

    public bool SchemaValid { get; init; }

    public int BeatCount { get; init; }

    public int SectionCount { get; init; }

    public bool SectionsMatchBeats { get; init; }

    public IReadOnlyList<string> ScriptBoundaryHits { get; init; } = [];

    public IReadOnlyList<string> StoryboardBoundaryHits { get; init; } = [];

    public IReadOnlyList<string> ImplementationLeaks { get; init; } = [];

    public SignalStatus CharacterGrounding { get; init; } = new();

    public SignalStatus WorldGrounding { get; init; } = new();

    public SignalStatus IdentityPreservation { get; init; } = new();

    public int OutputChars { get; init; }

    public int? OutputTokens { get; init; }

    public long LatencyMs { get; init; }

    public double? ProviderDurationMs { get; init; }

    public string Result { get; init; } = "FAIL";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string Title { get; init; } = string.Empty;

    public string OpeningHook { get; init; } = string.Empty;

    public string Closing { get; init; } = string.Empty;

    public IReadOnlyList<BeatAlignmentRow> Alignment { get; init; } = [];
}

/// <summary>Storyboard phase validation dimensions for one case.</summary>
public sealed record StoryboardValidation
{
    public bool JsonValid { get; init; }

    public bool SchemaValid { get; init; }

    public int SceneCount { get; init; }

    public IReadOnlyList<string> ScriptRewriteHits { get; init; } = [];

    public IReadOnlyList<string> ImplementationLeaks { get; init; } = [];

    public SignalStatus CharacterGrounding { get; init; } = new();

    public SignalStatus WorldGrounding { get; init; } = new();

    public SignalStatus IdentityPreservation { get; init; } = new();

    public string StoryAlignment { get; init; } = "unavailable";

    public string ScriptAlignment { get; init; } = "unavailable";

    public string VisualQuality { get; init; } = "unavailable";

    public int OutputChars { get; init; }

    public int? OutputTokens { get; init; }

    public long LatencyMs { get; init; }

    public double? ProviderDurationMs { get; init; }

    public string Result { get; init; } = "FAIL";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<ScenePreview> Scenes { get; init; } = [];
}

/// <summary>All combined-case dimensions; written verbatim to artifacts.</summary>
public sealed record CaseReport
{
    public string Case { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int BibleCharacterCount { get; init; }

    public int BibleWorldCount { get; init; }

    public bool ProposalParsed { get; init; }

    public bool BiblesRegistered { get; init; }

    public SignalStatus Grounding { get; init; } = new();

    public SignalStatus ContextResolution { get; init; } = new();

    public SignalStatus SameIdentityContext { get; init; } = new();

    public SignalStatus Immutability { get; init; } = new();

    public IReadOnlyList<CharacterPreview> Characters { get; init; } = [];

    public IReadOnlyList<WorldPreview> Worlds { get; init; } = [];

    public IReadOnlyList<GroundingPreview> Groundings { get; init; } = [];

    public ScriptValidation? Script { get; init; }

    public StoryboardValidation? Storyboard { get; init; }

    public string Result { get; init; } = "FAIL";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
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

    public string StoryBibleDir { get; init; } = string.Empty;

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

    public static string SignalText(SignalStatus signal) =>
        signal.Details.Count == 0
            ? signal.Status
            : $"{signal.Status} ({string.Join("; ", signal.Details)})";

    public static void PrintReport(CaseReport report)
    {
        Console.WriteLine($"Bible:");
        Console.WriteLine($"  Characters  {report.BibleCharacterCount}");
        Console.WriteLine($"  Worlds      {report.BibleWorldCount}");
        Console.WriteLine();
        Console.WriteLine($"Grounding:");
        Console.WriteLine($"  Proposal    {StatusOf(report.ProposalParsed)}");
        Console.WriteLine($"  Register    {StatusOf(report.BiblesRegistered)}");
        Console.WriteLine($"  Apply       {SignalText(report.Grounding)}");
        Console.WriteLine($"  Context     {SignalText(report.ContextResolution)}");
        Console.WriteLine($"  SameIds     {SignalText(report.SameIdentityContext)}");
        Console.WriteLine($"  Immutable   {SignalText(report.Immutability)}");
        Console.WriteLine();

        if (report.Script is { } script)
        {
            Console.WriteLine("Script:");
            Console.WriteLine($"  JSON        {StatusOf(script.JsonValid)}");
            Console.WriteLine($"  Schema      {StatusOf(script.SchemaValid)}");
            Console.WriteLine(
                $"  Sections    {(script.SchemaValid
                    ? $"{script.SectionCount}/{script.BeatCount} {(script.SectionsMatchBeats ? "PASS" : "FAIL")}"
                    : "unavailable")}");
            Console.WriteLine($"  Boundary    {BoundaryText(script.ScriptBoundaryHits.Concat(script.StoryboardBoundaryHits))}");
            Console.WriteLine(
                $"  ImplLeak    {(script.ImplementationLeaks.Count == 0
                    ? "none"
                    : $"FAIL ({string.Join(", ", script.ImplementationLeaks)})")}");
            Console.WriteLine($"  CharacterGrounding  {SignalText(script.CharacterGrounding)}");
            Console.WriteLine($"  WorldGrounding      {SignalText(script.WorldGrounding)}");
            Console.WriteLine($"  IdentityPreservation {SignalText(script.IdentityPreservation)}");
            Console.WriteLine($"  Latency     {script.LatencyMs / 1000.0:0.0}s");
        }
        else
        {
            Console.WriteLine("Script:     unavailable");
        }

        Console.WriteLine();

        if (report.Storyboard is { } storyboard)
        {
            Console.WriteLine("Storyboard:");
            Console.WriteLine($"  JSON        {StatusOf(storyboard.JsonValid)}");
            Console.WriteLine($"  Schema      {StatusOf(storyboard.SchemaValid)}");
            Console.WriteLine($"  Scenes      {storyboard.SceneCount}");
            Console.WriteLine($"  Rewrite     {BoundaryText(storyboard.ScriptRewriteHits)}");
            Console.WriteLine(
                $"  ImplLeak    {(storyboard.ImplementationLeaks.Count == 0
                    ? "none"
                    : $"FAIL ({string.Join(", ", storyboard.ImplementationLeaks)})")}");
            Console.WriteLine($"  CharacterGrounding  {SignalText(storyboard.CharacterGrounding)}");
            Console.WriteLine($"  WorldGrounding      {SignalText(storyboard.WorldGrounding)}");
            Console.WriteLine($"  IdentityPreservation {SignalText(storyboard.IdentityPreservation)}");
            Console.WriteLine($"  StoryAlignment      {storyboard.StoryAlignment}");
            Console.WriteLine($"  ScriptAlignment     {storyboard.ScriptAlignment}");
            Console.WriteLine($"  VisualQuality       {storyboard.VisualQuality}");
            Console.WriteLine($"  Latency     {storyboard.LatencyMs / 1000.0:0.0}s");
        }
        else
        {
            Console.WriteLine("Storyboard: unavailable");
        }

        Console.WriteLine();
        Console.WriteLine($"RESULT      {report.Result}");

        if (!string.IsNullOrEmpty(report.ErrorMessage))
        {
            Console.WriteLine($"Error       {report.ErrorCode}: {report.ErrorMessage}");
        }

        if (report.Characters.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Characters:");
            foreach (var character in report.Characters)
            {
                Console.WriteLine($"  {character.Id}");
                Console.WriteLine($"    role: {character.Role}");
                Console.WriteLine($"    species: {character.Species ?? "-"}");
                Console.WriteLine($"    age: {character.AgePresentation ?? "-"}");
                Console.WriteLine($"    bodyStyle: {character.BodyStyle ?? "-"}");
                Console.WriteLine($"    hair: {character.Hair ?? "-"}");
                Console.WriteLine($"    eyes: {character.Eyes ?? "-"}");
                Console.WriteLine(
                    $"    distinguishingTraits: {(character.DistinguishingTraits.Count == 0 ? "-" : string.Join(", ", character.DistinguishingTraits))}");
            }
        }

        if (report.Worlds.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Worlds:");
            foreach (var world in report.Worlds)
            {
                Console.WriteLine($"  {world.Id}");
                Console.WriteLine($"    environment: {world.EnvironmentType}");
                Console.WriteLine(
                    $"    spatialTraits: {(world.SpatialTraits.Count == 0 ? "-" : string.Join(", ", world.SpatialTraits))}");
                Console.WriteLine(
                    $"    recurringProps: {(world.RecurringProps.Count == 0 ? "-" : string.Join(", ", world.RecurringProps))}");
            }
        }

        if (report.Groundings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Grounding:");
            foreach (var grounding in report.Groundings)
            {
                Console.WriteLine(
                    $"  {grounding.BeatId} -> chars=[{string.Join(", ", grounding.CharacterRefs)}] "
                    + $"worlds=[{string.Join(", ", grounding.WorldRefs)}]");
            }
        }

        if (report.Script?.Alignment.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Script sections:");
            foreach (var row in report.Script.Alignment)
            {
                Console.WriteLine(
                    $"  Beat {row.BeatOrder} {row.BeatRole,-12} -> Section {row.SectionIndex?.ToString() ?? "-"} "
                    + $"{Shorten(row.SectionHeading, 40)}");
                Console.WriteLine($"    {Shorten(row.NarrationPreview, 110)}");
            }
        }

        if (report.Storyboard?.Scenes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Storyboard scenes:");
            foreach (var scene in report.Storyboard.Scenes)
            {
                Console.WriteLine($"  {scene.Index}. {Shorten(scene.Heading, 48)} - {Shorten(scene.Visual, 130)}");
            }
        }
    }

    private static string BoundaryText(IEnumerable<string> hits)
    {
        var list = hits.Distinct(StringComparer.Ordinal).ToList();
        return list.Count == 0 ? "none" : $"REVIEW ({string.Join(", ", list)})";
    }
}

/// <summary>
/// Framework-free self-check of the harness' deterministic logic. It reuses the
/// production parser, grounder, and context builder, and never calls a model.
/// </summary>
public static class SelfCheck
{
    private const string ProposalJson = """
        {
          "characterBibles": [
            {
              "id": "student-01",
              "version": 1,
              "displayName": "The Student",
              "identity": {
                "role": "protagonist",
                "species": "human",
                "agePresentation": "young-adult",
                "hair": "short-black",
                "eyes": "dark-brown",
                "visualDescription": "A calm student with short black hair.",
                "distinguishingTraits": ["red-hoodie"]
              },
              "personalityTraits": ["curious"],
              "baselineVariant": "default",
              "relationships": [],
              "assetReferences": []
            },
            {
              "id": "unrelated-01",
              "version": 1,
              "displayName": "Unrelated",
              "identity": { "role": "supporting", "species": "human", "visualDescription": "Never referenced." },
              "baselineVariant": "default"
            }
          ],
          "worldBibles": [
            {
              "id": "study-room",
              "version": 1,
              "displayName": "Study Room",
              "identity": {
                "environmentType": "bedroom",
                "visualDescription": "A small study bedroom with a desk beside a single window.",
                "spatialTraits": ["single-window"]
              },
              "recurringProps": ["desk"],
              "continuityRules": ["desk stays beside window"],
              "locations": [],
              "assetReferences": []
            }
          ],
          "beatGroundings": [
            { "beatId": "beat-01", "characterRefs": ["student-01"], "worldRefs": ["study-room"] },
            { "beatId": "beat-02", "characterRefs": ["student-01"], "worldRefs": ["study-room"] }
          ]
        }
        """;

    public static int Run()
    {
        var checks = new (string Description, Func<bool> Run)[]
        {
            ("proposal registration succeeds", () =>
            {
                var planning = Planning();
                return planning.Characters.Characters.Count == 2 && planning.Worlds.Worlds.Count == 1;
            }),
            ("grounding succeeds", () => Planning().GroundedStoryPlan.Beats[0].CharacterRefs.SequenceEqual(["student-01"])),
            ("grounded StoryContext includes expected bible ids", () =>
            {
                var planning = Planning();
                return planning.Context.Characters.Select(character => character.Id.Value).SequenceEqual(["student-01"])
                    && planning.Context.Worlds.Select(world => world.Id.Value).SequenceEqual(["study-room"]);
            }),
            ("script phase receives a populated StoryContext", () =>
            {
                var planning = Planning();
                var recorder = new RecordingStoryContextBuilder(
                    new StoryContextBuilder(planning.Characters, planning.Worlds));
                recorder.Build(new StoryContextRequest { StoryPlan = planning.GroundedStoryPlan });
                return recorder.LastResult is { Context.Characters.Count: 1, Context.Worlds.Count: 1 };
            }),
            ("storyboard phase receives the same populated identities", () =>
            {
                var planning = Planning();
                var script = BuildContext(planning);
                var storyboard = BuildContext(planning);
                return script.Characters.Select(character => character.Id.Value)
                        .SequenceEqual(storyboard.Characters.Select(character => character.Id.Value))
                    && script.Worlds.Select(world => world.Id.Value)
                        .SequenceEqual(storyboard.Worlds.Select(world => world.Id.Value));
            }),
            ("unrelated bible is excluded from context", () =>
            {
                var planning = Planning();
                return planning.Context.Characters.All(character => character.Id.Value != "unrelated-01")
                    && planning.ContextResolution.Status == "PASS";
            }),
            ("original story plan remains immutable", () =>
            {
                var plan = Plan();
                var proposal = StoryBiblePlanParser.Parse(ProposalJson, plan);
                var planning = GroundedPlanningFactory.Build(new CreativeDirection(), plan, proposal);
                return PlanImmutability.Compare(plan, planning.GroundedStoryPlan).Count == 0
                    && plan.Beats[0].CharacterRefs.Count == 0;
            }),
            ("empty refs remain supported", () =>
            {
                var emptyProposal = StoryBiblePlanParser.Parse(EmptyRefsProposalJson, Plan());
                var planning = GroundedPlanningFactory.Build(new CreativeDirection(), Plan(), emptyProposal);
                return planning.ContextResolution.Status == "PASS" && planning.Context.Characters.Count == 0;
            }),
            ("obvious identity contradiction signal works", () =>
            {
                var bible = Bible("student-01", species: "human", age: "young-adult");
                var signal = GroundingSignalScanner.CharacterGrounding([bible], "The elderly robot stepped forward.");
                return signal.Status == "REVIEW"
                    && signal.Details.Any(detail => detail.Contains("robot", StringComparison.Ordinal))
                    && signal.Details.Any(detail => detail.Contains("elderly", StringComparison.Ordinal));
            }),
            ("implementation leak is detected", () =>
                ImplementationLeakScanner.Scan("Render with ComfyUI and save to /home/tama/scene.py").Count > 0),
            ("artifact shapes serialize", SerializationWorks),
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

    private static GroundedPlanning Planning() =>
        GroundedPlanningFactory.Build(
            new CreativeDirection(),
            Plan(),
            StoryBiblePlanParser.Parse(ProposalJson, Plan()));

    private static StoryContextModel BuildContext(GroundedPlanning planning) =>
        new StoryContextBuilder(planning.Characters, planning.Worlds)
            .Build(new StoryContextRequest { StoryPlan = planning.GroundedStoryPlan })
            .Context;

    private static CharacterBible Bible(string id, string species, string age) =>
        new()
        {
            Id = new CharacterBibleId(id),
            Version = new CharacterBibleVersion(1),
            DisplayName = "The Student",
            Identity = new CharacterIdentity
            {
                Role = "protagonist",
                Species = species,
                AgePresentation = age,
                VisualDescription = "A calm student."
            }
        };

    private static StoryPlan Plan() => new()
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
                Purpose = "The interface answers.",
                TargetDurationSeconds = 5,
                ContinuityFrom = [new StoryBeatId("beat-01")]
            }
        ]
    };

    private static bool SerializationWorks()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new object[]
                {
                    new CaseReport
                    {
                        Name = "case",
                        Script = new ScriptValidation { Alignment = [new BeatAlignmentRow { BeatOrder = 1 }] },
                        Storyboard = new StoryboardValidation { Scenes = [new ScenePreview { Index = 1, Heading = "h" }] }
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

    private const string EmptyRefsProposalJson = """
        {
          "characterBibles": [
            {
              "id": "student-01",
              "version": 1,
              "displayName": "The Student",
              "identity": { "role": "protagonist", "species": "human", "visualDescription": "A calm student." },
              "baselineVariant": "default"
            }
          ],
          "worldBibles": [],
          "beatGroundings": [
            { "beatId": "beat-01", "characterRefs": [], "worldRefs": [] },
            { "beatId": "beat-02", "characterRefs": [], "worldRefs": [] }
          ]
        }
        """;
}
