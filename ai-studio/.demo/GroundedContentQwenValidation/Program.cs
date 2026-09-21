using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.Creative;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;
using AIStudio.GroundedContentValidation;
using AIStudio.Infrastructure.AI;
using AIStudio.Infrastructure.Gpu;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Real-Qwen Grounded Script + Storyboard validation harness. For each of the two
// grounded cases it rebuilds the grounded state deterministically from the saved
// Bible proposal + the ORIGINAL StoryPlan (fresh registries -> explicit register ->
// StoryPlanGrounder -> StoryContextBuilder), then drives the PRODUCTION
// GenerateScriptJobHandler and GenerateStoryboardJobHandler with that SAME grounded
// state and the real OllamaTextGenerator. No database, no durable job, no media.
const string Usage =
    """
    Grounded Script + Storyboard Real-Qwen Validation

    Usage:
      dotnet <harness>.dll --creative <dir> --story <dir> --bible <dir> --output <dir>
      dotnet <harness>.dll --self-check

    Configuration uses the same environment keys the API uses:
      Ollama__BaseUrl                 default http://127.0.0.1:11434
      Ollama__DefaultModel            required; the configured local model
      Ollama__TimeoutSeconds          default 300 (1..600)
      AISTUDIO_SCRIPT_LANGUAGE        default English
      AISTUDIO_CREATIVE_DIRECTIONS    creative-direction artifacts directory override
      AISTUDIO_STORY_DIRECTIONS       story-plan artifacts directory override
      AISTUDIO_STORY_BIBLE_DIRECTIONS story-bible artifacts directory override
      AISTUDIO_VALIDATION_OUTPUT      artifact directory override
    """;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(Usage);
    return 0;
}

if (args.Contains("--self-check"))
{
    return SelfCheck.Run();
}

var model = Environment.GetEnvironmentVariable("Ollama__DefaultModel");
if (string.IsNullOrWhiteSpace(model))
{
    Console.Error.WriteLine(
        "Ollama__DefaultModel is not set. Source the local .env or run through scripts/e2e-grounded-content-qwen.sh.");
    return 2;
}

var baseUrl = GetEnv("Ollama__BaseUrl") ?? "http://127.0.0.1:11434";
var timeoutSeconds = GetEnvInt("Ollama__TimeoutSeconds", 300, 1, 600);
var language = GetOption(args, "--language") ?? GetEnv("AISTUDIO_SCRIPT_LANGUAGE") ?? "English";

var creativeDirectory = GetOption(args, "--creative") ?? GetEnv("AISTUDIO_CREATIVE_DIRECTIONS");
var storyDirectory = GetOption(args, "--story") ?? GetEnv("AISTUDIO_STORY_DIRECTIONS");
var bibleDirectory = GetOption(args, "--bible") ?? GetEnv("AISTUDIO_STORY_BIBLE_DIRECTIONS");

foreach (var (directory, name) in new[]
{
    (creativeDirectory, "CreativeDirection"),
    (storyDirectory, "StoryPlan"),
    (bibleDirectory, "StoryBible")
})
{
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
    {
        Console.Error.WriteLine(
            $"{name} artifacts directory not found: '{directory}'. Run the upstream validation first or set the matching AISTUDIO_*_DIRECTIONS variable.");
        return 2;
    }
}

var creativeRoot = creativeDirectory!;
var storyRoot = storyDirectory!;
var bibleRoot = bibleDirectory!;

var outputDirectory = GetOption(args, "--output")
    ?? GetEnv("AISTUDIO_VALIDATION_OUTPUT")
    ?? Path.Combine(
        Directory.GetCurrentDirectory(),
        "artifacts",
        $"grounded-content-qwen-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}");

var patternRegistry = new NarrativePatternRegistry(SeedNarrativePatterns.All);

var httpClient = new HttpClient
{
    BaseAddress = new Uri($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute),
    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
};

var gpuGate = new GpuResourceGate(
    Options.Create(new GpuResourceGateOptions { Enabled = true }),
    NullLogger<GpuResourceGate>.Instance);

IAiTextGenerator provider = new OllamaTextGenerator(
    httpClient,
    Options.Create(new OllamaOptions
    {
        BaseUrl = baseUrl,
        DefaultModel = model,
        TimeoutSeconds = timeoutSeconds
    }),
    gpuGate,
    NullLogger<OllamaTextGenerator>.Instance);

var startedAt = DateTimeOffset.UtcNow;
Directory.CreateDirectory(outputDirectory);

Console.WriteLine("Grounded Script + Storyboard Real-Qwen Validation");
Console.WriteLine($"  model:     {model}");
Console.WriteLine($"  base-url:  {baseUrl}");
Console.WriteLine($"  timeout:   {timeoutSeconds}s");
Console.WriteLine($"  language:  {language}");
Console.WriteLine($"  creative:  {creativeRoot}");
Console.WriteLine($"  story:     {storyRoot}");
Console.WriteLine($"  bible:     {bibleRoot}");
Console.WriteLine($"  output:    {outputDirectory}");
Console.WriteLine();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var reports = new List<CaseReport>();

try
{
    for (var index = 0; index < CaseCatalog.Cases.Count; index++)
    {
        var setup = CaseCatalog.Cases[index];
        var report = await RunCaseAsync(index, setup, cancellation.Token);
        reports.Add(report);

        Console.WriteLine($"Case: {setup.Name}");
        ValidationOutput.PrintReport(report);
        Console.WriteLine();
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled before all cases completed.");
    return 130;
}

var pass = reports.Count(report => report.Result == "PASS");
var review = reports.Count(report => report.Result == "REVIEW");
var fail = reports.Count(report => report.Result == "FAIL");
var overall = fail > 0 ? "FAIL" : review > 0 ? "REVIEW" : "PASS";

var summary = new RunSummary
{
    StartedAtUtc = startedAt.ToString("O"),
    FinishedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
    Model = model,
    BaseUrl = baseUrl,
    TimeoutSeconds = timeoutSeconds,
    CreativeDirectionDir = creativeRoot,
    StoryPlanDir = storyRoot,
    StoryBibleDir = bibleRoot,
    Pass = pass,
    Review = review,
    Fail = fail,
    Overall = overall,
    Cases = reports
};

await WriteJsonAsync(Path.Combine(outputDirectory, "summary.json"), summary);

Console.WriteLine("Summary");
Console.WriteLine($"PASS: {pass}");
Console.WriteLine($"REVIEW: {review}");
Console.WriteLine($"FAIL: {fail}");
Console.WriteLine($"Overall: {overall}");
Console.WriteLine();
Console.WriteLine("Artifacts:");
Console.WriteLine(outputDirectory);

return fail == reports.Count && reports.Count > 0 ? 1 : 0;

async Task<CaseReport> RunCaseAsync(
    int index,
    ValidationCaseSetup setup,
    CancellationToken cancellationToken)
{
    var caseDirectory = Path.Combine(outputDirectory, $"case-{index + 1}-{setup.Name}");
    Directory.CreateDirectory(caseDirectory);

    var label = $"case-{index + 1}-{setup.Name}";

    CreativeDirection direction;
    StoryPlan plan;
    StoryBiblePlan proposal;

    try
    {
        var directionJson = await File.ReadAllTextAsync(
            Path.Combine(creativeRoot, setup.CreativeDirectory, "creative-direction.json"),
            cancellationToken);
        var storyJson = await File.ReadAllTextAsync(
            Path.Combine(storyRoot, setup.StoryDirectory, "story-plan.json"),
            cancellationToken);
        var proposalJson = await File.ReadAllTextAsync(
            Path.Combine(bibleRoot, setup.StoryBibleDirectory, "story-bible-plan.json"),
            cancellationToken);

        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "creative-direction.json"), directionJson, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "story-plan-original.json"), storyJson, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "story-bible-plan.json"), proposalJson, cancellationToken);

        direction = CreativeDirectionParser.Parse(directionJson);
        plan = StoryPlanParser.Parse(storyJson, patternRegistry);
        proposal = StoryBiblePlanParser.Parse(proposalJson, plan);
    }
    catch (Exception exception)
    {
        var failed = new CaseReport
        {
            Case = label,
            Name = setup.Name,
            Result = "FAIL",
            ErrorCode = exception is StoryBiblePlanningException planning
                ? planning.Code
                : "harness_input_invalid",
            ErrorMessage = exception.Message
        };

        await WriteJsonAsync(Path.Combine(caseDirectory, "case-validation.json"), failed);
        return failed;
    }

    // Deterministic rebuild: fresh registries -> explicit register -> grounder ->
    // context. The SAME registry instances back both Script and Storyboard.
    GroundedPlanning grounded;
    try
    {
        grounded = GroundedPlanningFactory.Build(direction, plan, proposal);
    }
    catch (Exception exception)
    {
        var failed = new CaseReport
        {
            Case = label,
            Name = setup.Name,
            BibleCharacterCount = proposal.CharacterBibles.Count,
            BibleWorldCount = proposal.WorldBibles.Count,
            ProposalParsed = true,
            Result = "FAIL",
            ErrorCode = exception is StoryPlanGroundingException groundingError
                ? groundingError.Code
                : "grounding_failed",
            ErrorMessage = exception.Message
        };

        await WriteJsonAsync(Path.Combine(caseDirectory, "case-validation.json"), failed);
        return failed;
    }

    await WriteJsonAsync(Path.Combine(caseDirectory, "grounded-story-plan.json"), grounded.GroundedStoryPlan);
    await WriteJsonAsync(Path.Combine(caseDirectory, "story-context.json"), grounded.Context);

    var groundingSignal = grounded.ContextResolution.Status == "PASS"
        ? new SignalStatus
        {
            Status = "PASS",
            Details = [$"{proposal.BeatGroundings.Count} beat(s) grounded"]
        }
        : grounded.ContextResolution;

    var planDifferences = PlanImmutability.Compare(plan, grounded.GroundedStoryPlan);
    var immutability = planDifferences.Count == 0
        ? new SignalStatus { Status = "PASS", Details = ["only beat refs differ"] }
        : new SignalStatus { Status = "FAIL", Details = planDifferences };

    var projectId = Guid.NewGuid();
    var idea = new GenerateIdeaResult(
        direction.Concept.Title,
        direction.Treatment.HookTreatment,
        direction.Concept.Description,
        direction.Treatment.StoryApproach,
        direction.Concept.Audience,
        direction.Concept.Format);

    var reader = new StaticContentProjectReader(
        new ContentProjectSnapshot(
            projectId,
            $"Grounded validation: {setup.Name}",
            direction.Concept.Description));

    // Phase 1 — Script. The production handler builds StoryContext through the
    // injected builder, which is backed by the SAME populated registries.
    var scriptPayload = new GenerateScriptJobPayload(
        projectId,
        idea,
        language,
        direction,
        grounded.GroundedStoryPlan);
    var scriptPayloadJson = JsonSerializer.Serialize(scriptPayload, ValidationArtifacts.Json);

    var scriptAi = new RecordingAiTextGenerator(provider);
    var scriptContext = new RecordingStoryContextBuilder(
        new StoryContextBuilder(grounded.Characters, grounded.Worlds));
    var scriptHandler = new GenerateScriptJobHandler(reader, scriptAi, scriptContext);

    scriptAi.Reset();
    scriptContext.Reset();

    var scriptStopwatch = Stopwatch.StartNew();
    string? scriptResultJson = null;
    Exception? scriptError = null;

    try
    {
        scriptResultJson = await scriptHandler.ExecuteAsync(
            new ClaimedJob(
                Guid.NewGuid(),
                projectId,
                JobType.GenerateScript,
                "input-v1",
                scriptPayloadJson,
                0,
                2,
                false),
            cancellationToken);
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        scriptError = exception;
    }

    scriptStopwatch.Stop();

    await WritePromptAsync(caseDirectory, "script-prompt.txt", scriptAi.LastRequest, cancellationToken);
    var scriptRaw = scriptAi.LastResponse?.Text ?? string.Empty;
    await File.WriteAllTextAsync(Path.Combine(caseDirectory, "script-raw-response.txt"), scriptRaw, cancellationToken);

    GenerateScriptResult? script = null;
    if (scriptResultJson is not null)
    {
        try
        {
            script = GenerateScriptResult.Deserialize(scriptResultJson);
        }
        catch (Exception)
        {
            script = null;
        }

        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "script-result.json"), scriptResultJson, cancellationToken);
    }

    var scriptValidation = ValidateScript(
        script,
        scriptRaw,
        plan,
        proposal.CharacterBibles,
        proposal.WorldBibles,
        scriptError,
        scriptStopwatch.ElapsedMilliseconds,
        scriptAi.LastResponse?.OutputTokenCount,
        scriptAi.LastResponse?.TotalDuration?.TotalMilliseconds);

    await WriteJsonAsync(Path.Combine(caseDirectory, "script-validation.json"), scriptValidation);

    // Phase 2 — Storyboard consumes the freshly generated Script plus the SAME
    // grounded planning state (same registries, same grounded StoryPlan).
    StoryboardValidation? storyboardValidation = null;
    SignalStatus sameIdentity = new()
    {
        Status = "unavailable",
        Details = ["script phase did not complete"]
    };

    if (script is not null)
    {
        var reviewedScript = ReviewedScript.Create(projectId, Guid.NewGuid(), script.Serialize(), DateTimeOffset.UtcNow);
        reviewedScript.Approve(DateTimeOffset.UtcNow.AddMinutes(1));
        var scriptRepository = new StaticScriptReviewRepository(reviewedScript);

        var storyboardPayload = new GenerateStoryboardJobPayload(projectId, direction, grounded.GroundedStoryPlan);
        var storyboardPayloadJson = JsonSerializer.Serialize(storyboardPayload, ValidationArtifacts.Json);

        var storyboardAi = new RecordingAiTextGenerator(provider);
        var storyboardContext = new RecordingStoryContextBuilder(
            new StoryContextBuilder(grounded.Characters, grounded.Worlds));
        var storyboardHandler = new GenerateStoryboardJobHandler(
            reader,
            scriptRepository,
            storyboardAi,
            storyboardContext);

        storyboardAi.Reset();
        storyboardContext.Reset();

        var storyboardStopwatch = Stopwatch.StartNew();
        string? storyboardResultJson = null;
        Exception? storyboardError = null;

        try
        {
            storyboardResultJson = await storyboardHandler.ExecuteAsync(
                new ClaimedJob(
                    Guid.NewGuid(),
                    projectId,
                    JobType.GenerateStoryboard,
                    "input-v1",
                    storyboardPayloadJson,
                    0,
                    2,
                    false),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            storyboardError = exception;
        }

        storyboardStopwatch.Stop();

        await WritePromptAsync(caseDirectory, "storyboard-prompt.txt", storyboardAi.LastRequest, cancellationToken);
        var storyboardRaw = storyboardAi.LastResponse?.Text ?? string.Empty;
        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "storyboard-raw-response.txt"), storyboardRaw, cancellationToken);

        GenerateStoryboardResult? storyboard = null;
        if (storyboardResultJson is not null)
        {
            try
            {
                storyboard = GenerateStoryboardResult.Deserialize(storyboardResultJson);
            }
            catch (Exception)
            {
                storyboard = null;
            }

            await File.WriteAllTextAsync(Path.Combine(caseDirectory, "storyboard-result.json"), storyboardResultJson, cancellationToken);
        }

        storyboardValidation = ValidateStoryboard(
            storyboard,
            storyboardRaw,
            proposal.CharacterBibles,
            proposal.WorldBibles,
            storyboardError,
            storyboardStopwatch.ElapsedMilliseconds,
            storyboardAi.LastResponse?.OutputTokenCount,
            storyboardAi.LastResponse?.TotalDuration?.TotalMilliseconds);

        await WriteJsonAsync(Path.Combine(caseDirectory, "storyboard-validation.json"), storyboardValidation);

        sameIdentity = CompareContexts(
            scriptContext.LastResult?.Context,
            storyboardContext.LastResult?.Context,
            grounded.Context);
    }

    var objectiveValid = grounded.ContextResolution.Status == "PASS"
        && immutability.Status == "PASS"
        && scriptValidation.Result != "FAIL"
        && storyboardValidation is not null
        && storyboardValidation.Result != "FAIL"
        && sameIdentity.Status == "PASS";

    var reviewSignals = scriptValidation.Result == "REVIEW"
        || storyboardValidation?.Result == "REVIEW"
        || sameIdentity.Status != "PASS";

    var report = new CaseReport
    {
        Case = label,
        Name = setup.Name,
        BibleCharacterCount = proposal.CharacterBibles.Count,
        BibleWorldCount = proposal.WorldBibles.Count,
        ProposalParsed = true,
        BiblesRegistered = true,
        Grounding = groundingSignal,
        ContextResolution = grounded.ContextResolution,
        SameIdentityContext = sameIdentity,
        Immutability = immutability,
        Characters = proposal.CharacterBibles.Select(ToCharacterPreview).ToList(),
        Worlds = proposal.WorldBibles.Select(ToWorldPreview).ToList(),
        Groundings = proposal.BeatGroundings
            .Select(grounding => new GroundingPreview
            {
                BeatId = grounding.BeatId.Value,
                CharacterRefs = grounding.CharacterRefs,
                WorldRefs = grounding.WorldRefs
            })
            .ToList(),
        Script = scriptValidation,
        Storyboard = storyboardValidation,
        Result = ResultPolicy.Compute(objectiveValid, reviewSignals),
        ErrorCode = scriptError is JobExecutionException scriptJob
            ? scriptJob.ErrorCode
            : scriptError?.GetType().Name,
        ErrorMessage = scriptError?.Message
    };

    await WriteJsonAsync(Path.Combine(caseDirectory, "case-validation.json"), report);

    return report;
}

ScriptValidation ValidateScript(
    GenerateScriptResult? script,
    string rawResponse,
    StoryPlan plan,
    IReadOnlyList<CharacterBible> characters,
    IReadOnlyList<WorldBible> worlds,
    Exception? error,
    long latencyMs,
    int? outputTokens,
    double? providerDurationMs)
{
    var jsonValid = ValidationOutput.TryParseJsonObject(rawResponse);
    var schemaValid = script is not null;

    var scriptText = script is null ? string.Empty : BuildScriptText(script);
    var spokenText = script is null ? string.Empty : BuildSpokenText(script);

    IReadOnlyList<string> scriptBoundaryHits = script is null ? [] : ScriptBoundaryScanner.Scan(spokenText);
    IReadOnlyList<string> storyboardBoundaryHits = script is null ? [] : StoryboardBoundaryScanner.Scan(spokenText);
    IReadOnlyList<string> leaks = script is null ? [] : ImplementationLeakScanner.Scan(rawResponse);

    var characterGrounding = schemaValid
        ? GroundingSignalScanner.CharacterGrounding(characters, scriptText)
        : new SignalStatus { Status = "unavailable" };
    var worldGrounding = schemaValid
        ? GroundingSignalScanner.WorldGrounding(worlds, scriptText)
        : new SignalStatus { Status = "unavailable" };
    var preservation = GroundingSignalScanner.IdentityPreservation(characterGrounding, worldGrounding);

    var beatCount = plan.Beats.Count;
    var sectionCount = script?.Sections.Count ?? 0;
    var sectionsMatch = schemaValid && sectionCount == beatCount;
    var boundaryHits = scriptBoundaryHits.Count + storyboardBoundaryHits.Count;

    var objectiveValid = jsonValid && schemaValid && sectionsMatch && leaks.Count == 0 && boundaryHits < 3;
    var reviewSignals = boundaryHits > 0
        || characterGrounding.Status == "REVIEW"
        || worldGrounding.Status == "REVIEW";

    var orderedBeats = plan.Beats.OrderBy(beat => beat.Order).ToList();
    var alignment = new List<BeatAlignmentRow>(orderedBeats.Count);
    for (var beatIndex = 0; beatIndex < orderedBeats.Count; beatIndex++)
    {
        var beat = orderedBeats[beatIndex];
        var section = script is not null && beatIndex < script.Sections.Count
            ? script.Sections[beatIndex]
            : null;

        alignment.Add(new BeatAlignmentRow
        {
            BeatOrder = beat.Order,
            BeatRole = beat.Role.Value,
            BeatPurpose = beat.Purpose,
            SectionIndex = section is null ? null : beatIndex + 1,
            SectionHeading = section?.Heading,
            NarrationPreview = section?.Narration
        });
    }

    return new ScriptValidation
    {
        JsonValid = jsonValid,
        SchemaValid = schemaValid,
        BeatCount = beatCount,
        SectionCount = sectionCount,
        SectionsMatchBeats = sectionsMatch,
        ScriptBoundaryHits = scriptBoundaryHits,
        StoryboardBoundaryHits = storyboardBoundaryHits,
        ImplementationLeaks = leaks,
        CharacterGrounding = characterGrounding,
        WorldGrounding = worldGrounding,
        IdentityPreservation = preservation,
        OutputChars = rawResponse.Length,
        OutputTokens = outputTokens,
        LatencyMs = latencyMs,
        ProviderDurationMs = providerDurationMs,
        Result = ResultPolicy.Compute(objectiveValid, reviewSignals),
        ErrorCode = error is JobExecutionException job ? job.ErrorCode : error?.GetType().Name,
        ErrorMessage = error?.Message,
        Title = script?.Title ?? string.Empty,
        OpeningHook = script?.OpeningHook ?? string.Empty,
        Closing = script?.Closing ?? string.Empty,
        Alignment = alignment
    };
}

StoryboardValidation ValidateStoryboard(
    GenerateStoryboardResult? storyboard,
    string rawResponse,
    IReadOnlyList<CharacterBible> characters,
    IReadOnlyList<WorldBible> worlds,
    Exception? error,
    long latencyMs,
    int? outputTokens,
    double? providerDurationMs)
{
    var jsonValid = ValidationOutput.TryParseJsonObject(rawResponse);
    var schemaValid = storyboard is not null;

    var sceneText = storyboard is null
        ? string.Empty
        : string.Join(' ', storyboard.Scenes.Select(scene => $"{scene.Heading} {scene.Visual}"));

    IReadOnlyList<string> rewriteHits = storyboard is null ? [] : ScriptRewriteScanner.Scan(sceneText);
    IReadOnlyList<string> leaks = storyboard is null ? [] : ImplementationLeakScanner.Scan(rawResponse);

    var characterGrounding = schemaValid
        ? GroundingSignalScanner.CharacterGrounding(characters, sceneText)
        : new SignalStatus { Status = "unavailable" };
    var worldGrounding = schemaValid
        ? GroundingSignalScanner.WorldGrounding(worlds, sceneText)
        : new SignalStatus { Status = "unavailable" };
    var preservation = GroundingSignalScanner.IdentityPreservation(characterGrounding, worldGrounding);

    var objectiveValid = jsonValid && schemaValid && leaks.Count == 0;
    var reviewSignals = rewriteHits.Count > 0
        || characterGrounding.Status == "REVIEW"
        || worldGrounding.Status == "REVIEW";

    return new StoryboardValidation
    {
        JsonValid = jsonValid,
        SchemaValid = schemaValid,
        SceneCount = storyboard?.Scenes.Count ?? 0,
        ScriptRewriteHits = rewriteHits,
        ImplementationLeaks = leaks,
        CharacterGrounding = characterGrounding,
        WorldGrounding = worldGrounding,
        IdentityPreservation = preservation,
        StoryAlignment = schemaValid ? "REVIEW" : "unavailable",
        ScriptAlignment = schemaValid ? "REVIEW" : "unavailable",
        VisualQuality = schemaValid ? "REVIEW" : "unavailable",
        OutputChars = rawResponse.Length,
        OutputTokens = outputTokens,
        LatencyMs = latencyMs,
        ProviderDurationMs = providerDurationMs,
        Result = ResultPolicy.Compute(objectiveValid, reviewSignals),
        ErrorCode = error is JobExecutionException job ? job.ErrorCode : error?.GetType().Name,
        ErrorMessage = error?.Message,
        Title = storyboard?.Title ?? string.Empty,
        Scenes = ToScenePreviews(storyboard)
    };
}

static SignalStatus CompareContexts(
    AIStudio.Application.StoryContext.StoryContext? scriptContext,
    AIStudio.Application.StoryContext.StoryContext? storyboardContext,
    AIStudio.Application.StoryContext.StoryContext canonical)
{
    if (scriptContext is null || storyboardContext is null)
    {
        return new SignalStatus { Status = "FAIL", Details = ["a phase did not project a StoryContext"] };
    }

    var scriptCharacters = scriptContext.Characters.Select(character => character.Id.Value).ToList();
    var scriptWorlds = scriptContext.Worlds.Select(world => world.Id.Value).ToList();
    var storyboardCharacters = storyboardContext.Characters.Select(character => character.Id.Value).ToList();
    var storyboardWorlds = storyboardContext.Worlds.Select(world => world.Id.Value).ToList();
    var canonicalCharacters = canonical.Characters.Select(character => character.Id.Value).ToList();
    var canonicalWorlds = canonical.Worlds.Select(world => world.Id.Value).ToList();

    var same = scriptCharacters.SequenceEqual(storyboardCharacters)
        && scriptWorlds.SequenceEqual(storyboardWorlds)
        && scriptCharacters.SequenceEqual(canonicalCharacters)
        && scriptWorlds.SequenceEqual(canonicalWorlds);

    return same
        ? new SignalStatus
        {
            Status = "PASS",
            Details = [$"script=storyboard chars=[{string.Join(", ", scriptCharacters)}] worlds=[{string.Join(", ", scriptWorlds)}]"]
        }
        : new SignalStatus
        {
            Status = "FAIL",
            Details =
            [
                $"script chars=[{string.Join(", ", scriptCharacters)}] worlds=[{string.Join(", ", scriptWorlds)}]",
                $"storyboard chars=[{string.Join(", ", storyboardCharacters)}] worlds=[{string.Join(", ", storyboardWorlds)}]"
            ]
        };
}

static IReadOnlyList<ScenePreview> ToScenePreviews(GenerateStoryboardResult? storyboard)
{
    if (storyboard is null)
    {
        return [];
    }

    var scenes = new List<ScenePreview>(storyboard.Scenes.Count);
    for (var sceneIndex = 0; sceneIndex < storyboard.Scenes.Count; sceneIndex++)
    {
        var scene = storyboard.Scenes[sceneIndex];
        scenes.Add(new ScenePreview
        {
            Index = sceneIndex + 1,
            Heading = scene.Heading,
            Visual = scene.Visual
        });
    }

    return scenes;
}

static CharacterPreview ToCharacterPreview(CharacterBible bible)
{
    var identity = bible.Identity ?? new CharacterIdentity();

    return new CharacterPreview
    {
        Id = bible.Id.Value,
        DisplayName = bible.DisplayName,
        Role = identity.Role,
        Species = identity.Species,
        AgePresentation = identity.AgePresentation,
        BodyStyle = identity.BodyStyle,
        Hair = identity.Hair,
        Eyes = identity.Eyes,
        DistinguishingTraits = identity.DistinguishingTraits,
        PersonalityTraits = bible.PersonalityTraits,
        VisualDescription = identity.VisualDescription
    };
}

static WorldPreview ToWorldPreview(WorldBible bible)
{
    var identity = bible.Identity ?? new WorldIdentity();

    return new WorldPreview
    {
        Id = bible.Id.Value,
        DisplayName = bible.DisplayName,
        EnvironmentType = identity.EnvironmentType,
        SpatialTraits = identity.SpatialTraits,
        RecurringProps = bible.RecurringProps,
        VisualDescription = identity.VisualDescription
    };
}

static string BuildScriptText(GenerateScriptResult script) =>
    string.Join(
        ' ',
        script.Title,
        script.OpeningHook,
        string.Join(' ', script.Sections.Select(section => $"{section.Heading} {section.Narration}")),
        script.Closing);

static string BuildSpokenText(GenerateScriptResult script) =>
    string.Join(
        ' ',
        script.OpeningHook,
        string.Join(' ', script.Sections.Select(section => section.Narration)),
        script.Closing);

static async Task WritePromptAsync(
    string directory,
    string fileName,
    AiTextRequest? request,
    CancellationToken cancellationToken)
{
    if (request is null)
    {
        return;
    }

    var prompt = $"[system]{Environment.NewLine}{request.SystemPrompt}"
        + $"{Environment.NewLine}{Environment.NewLine}[user]{Environment.NewLine}{request.Prompt}";
    await File.WriteAllTextAsync(Path.Combine(directory, fileName), prompt, cancellationToken);
}

static async Task WriteJsonAsync<T>(string path, T value)
{
    var json = JsonSerializer.Serialize(value, ValidationArtifacts.Json);
    await File.WriteAllTextAsync(path, json + Environment.NewLine);
}

static string? GetOption(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], name, StringComparison.Ordinal))
        {
            return arguments[index + 1];
        }
    }

    return null;
}

static string? GetEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

static int GetEnvInt(string name, int fallback, int minimum, int maximum)
{
    var raw = GetEnv(name);
    if (raw is null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
    {
        return fallback;
    }

    return Math.Clamp(value, minimum, maximum);
}
