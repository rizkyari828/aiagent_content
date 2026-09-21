using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Bibles;
using AIStudio.Application.Creative;
using AIStudio.Application.StoryContext;
using AIStudio.Application.Stories;
using AIStudio.Infrastructure.AI;
using AIStudio.Infrastructure.Gpu;
using AIStudio.StoryBibleValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Real-Qwen Story Bible Planner validation harness. It drives the PRODUCTION
// IStoryBiblePlanner (QwenStoryBiblePlanner over the real OllamaTextGenerator),
// validates the proposal with the production parser and validators, then applies it
// explicitly through the deterministic StoryPlanGrounder and StoryContextBuilder.
// It never registers anything in production, persists nothing, mutates no StoryPlan,
// and calls no Script or Storyboard stage.
const string Usage =
    """
    Story Bible Planner Real-Qwen Validation

    Usage:
      dotnet <harness>.dll --creative <dir> --story <dir> --output <dir>
      dotnet <harness>.dll --self-check

    Configuration uses the same environment keys the API uses:
      Ollama__BaseUrl                 default http://127.0.0.1:11434
      Ollama__DefaultModel            required; the configured local model
      Ollama__TimeoutSeconds          default 300 (1..600)
      AISTUDIO_CREATIVE_DIRECTIONS    creative-direction artifacts directory override
      AISTUDIO_STORY_DIRECTIONS       story-plan artifacts directory override
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
        "Ollama__DefaultModel is not set. Source the local .env or run through scripts/e2e-story-bible-qwen.sh.");
    return 2;
}

var baseUrl = GetEnv("Ollama__BaseUrl") ?? "http://127.0.0.1:11434";
var timeoutSeconds = GetEnvInt("Ollama__TimeoutSeconds", 300, 1, 600);

var creativeDirectory = GetOption(args, "--creative") ?? GetEnv("AISTUDIO_CREATIVE_DIRECTIONS");
var storyDirectory = GetOption(args, "--story") ?? GetEnv("AISTUDIO_STORY_DIRECTIONS");

foreach (var (directory, name) in new[]
{
    (creativeDirectory, "CreativeDirection"),
    (storyDirectory, "StoryPlan")
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

var outputDirectory = GetOption(args, "--output")
    ?? GetEnv("AISTUDIO_VALIDATION_OUTPUT")
    ?? Path.Combine(
        Directory.GetCurrentDirectory(),
        "artifacts",
        $"story-bible-qwen-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}");

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

Console.WriteLine("Story Bible Planner Real-Qwen Validation");
Console.WriteLine($"  model:     {model}");
Console.WriteLine($"  base-url:  {baseUrl}");
Console.WriteLine($"  timeout:   {timeoutSeconds}s");
Console.WriteLine($"  creative:  {creativeDirectory}");
Console.WriteLine($"  story:     {storyDirectory}");
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

        Console.WriteLine($"Case {index + 1}: {setup.Name}");
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

    try
    {
        var directionJson = await File.ReadAllTextAsync(
            Path.Combine(creativeRoot, setup.DirectoryName, "creative-direction.json"),
            cancellationToken);
        var storyJson = await File.ReadAllTextAsync(
            Path.Combine(storyRoot, setup.DirectoryName, "story-plan.json"),
            cancellationToken);

        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "creative-direction.json"), directionJson, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "story-plan-original.json"), storyJson, cancellationToken);

        direction = CreativeDirectionParser.Parse(directionJson);
        plan = StoryPlanParser.Parse(storyJson, patternRegistry);
    }
    catch (Exception exception)
    {
        return new CaseReport
        {
            Case = label,
            Name = setup.Name,
            Result = "FAIL",
            ErrorCode = "harness_input_invalid",
            ErrorMessage = exception.Message
        };
    }

    // Snapshot the original refs so an accidental StoryPlan mutation is detectable
    // after grounding (the grounder must return a NEW plan).
    var originalRefs = plan.Beats
        .Select(beat => (beat.Id.Value, string.Join(',', beat.CharacterRefs), string.Join(',', beat.WorldRefs)))
        .ToList();

    var recorder = new RecordingAiTextGenerator(provider);
    IStoryBiblePlanner planner = new QwenStoryBiblePlanner(recorder);
    recorder.Reset();

    var stopwatch = Stopwatch.StartNew();
    StoryBiblePlan? proposal = null;
    Exception? error = null;

    try
    {
        proposal = await planner.BuildAsync(
            new StoryBiblePlanningRequest { CreativeDirection = direction, StoryPlan = plan },
            cancellationToken);
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        error = exception;
    }

    stopwatch.Stop();

    var request = recorder.LastRequest;
    if (request is not null)
    {
        var prompt = $"[system]{Environment.NewLine}{request.SystemPrompt}"
            + $"{Environment.NewLine}{Environment.NewLine}[user]{Environment.NewLine}{request.Prompt}";
        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "prompt.txt"), prompt, cancellationToken);
    }

    var rawResponse = recorder.LastResponse?.Text ?? string.Empty;
    await File.WriteAllTextAsync(Path.Combine(caseDirectory, "raw-response.txt"), rawResponse, cancellationToken);

    var jsonValid = ValidationOutput.TryParseJsonObject(rawResponse);
    var leaks = ImplementationLeakScanner.Scan(rawResponse);

    if (proposal is null)
    {
        var failed = new CaseReport
        {
            Case = label,
            Name = setup.Name,
            JsonValid = jsonValid,
            SchemaValid = false,
            ImplementationLeaks = leaks,
            OutputChars = rawResponse.Length,
            OutputTokens = recorder.LastResponse?.OutputTokenCount,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            ProviderDurationMs = recorder.LastResponse?.TotalDuration?.TotalMilliseconds,
            Result = "FAIL",
            ErrorCode = error is StoryBiblePlanningException planning
                ? planning.Code
                : error?.GetType().Name,
            ErrorMessage = error?.Message
        };

        await WriteJsonAsync(Path.Combine(caseDirectory, "validation.json"), failed);
        return failed;
    }

    await WriteJsonAsync(Path.Combine(caseDirectory, "story-bible-plan.json"), proposal);

    var grounding = ProposalInspector.Inspect(proposal, plan);
    var characterIds = proposal.CharacterBibles.Select(bible => bible.Id.Value).ToList();
    var worldIds = proposal.WorldBibles.Select(bible => bible.Id.Value).ToList();

    var characterReuse = ReuseAnalyzer.Analyze(characterIds, proposal.BeatGroundings, character: true);
    var worldReuse = ReuseAnalyzer.Analyze(worldIds, proposal.BeatGroundings, character: false);

    var fragmentation = IdentityFragmentationScanner.Scan(characterIds).Select(id => $"char:{id}")
        .Concat(IdentityFragmentationScanner.Scan(worldIds).Select(id => $"world:{id}"))
        .ToList();

    var identityQuality = proposal.CharacterBibles
        .SelectMany(bible => IdentityQualityScanner.Scan(bible).Select(detail => $"{bible.Id.Value}:{detail}"))
        .ToList();

    var worldIdentityQuality = proposal.WorldBibles
        .SelectMany(bible => WorldIdentityQualityScanner.Scan(bible).Select(detail => $"{bible.Id.Value}:{detail}"))
        .ToList();

    var assetReferences = AssetReferencePolicy.Evaluate(proposal, leaks);

    // Phase 2 — explicit materialization: fresh in-memory registries, explicit
    // registration, then the existing deterministic StoryPlanGrounder. Nothing is
    // persisted and the original StoryPlan is never edited.
    var characters = new CharacterBibleRegistry(proposal.CharacterBibles);
    var worlds = new WorldBibleRegistry(proposal.WorldBibles);

    StoryPlan? grounded = null;
    var groundApply = new SignalStatus();

    try
    {
        grounded = new StoryPlanGrounder(characters, worlds).Ground(
            new StoryPlanGroundingRequest { StoryPlan = plan, Beats = proposal.BeatGroundings });

        groundApply = new SignalStatus
        {
            Status = "PASS",
            Details = [$"{proposal.BeatGroundings.Count} beat(s) grounded"]
        };
    }
    catch (StoryPlanGroundingException exception)
    {
        groundApply = new SignalStatus { Status = "FAIL", Details = [exception.Code] };
        error = exception;
    }
    catch (Exception exception)
    {
        groundApply = new SignalStatus { Status = "FAIL", Details = [exception.GetType().Name] };
        error = exception;
    }

    var immutability = new SignalStatus { Status = "unavailable" };

    if (grounded is not null)
    {
        await WriteJsonAsync(Path.Combine(caseDirectory, "grounded-story-plan.json"), grounded);

        var differences = StoryPlanImmutability.Compare(plan, grounded).ToList();
        var mutatedRefs = plan.Beats
            .Select(beat => (beat.Id.Value, string.Join(',', beat.CharacterRefs), string.Join(',', beat.WorldRefs)))
            .ToList();

        if (!originalRefs.SequenceEqual(mutatedRefs))
        {
            differences.Add("originalPlanRefsMutated");
        }

        immutability = differences.Count == 0
            ? new SignalStatus { Status = "PASS", Details = ["only beat refs differ"] }
            : new SignalStatus { Status = "FAIL", Details = differences };
    }

    // Phase 3 — project the grounded plan through the real StoryContextBuilder.
    var storyContext = new SignalStatus { Status = "unavailable" };

    if (grounded is not null)
    {
        var referencedCharacters = grounded.Beats
            .SelectMany(beat => beat.CharacterRefs ?? [])
            .Where(StoryIdentifier.IsValid)
            .ToHashSet(StringComparer.Ordinal);
        var referencedWorlds = grounded.Beats
            .SelectMany(beat => beat.WorldRefs ?? [])
            .Where(StoryIdentifier.IsValid)
            .ToHashSet(StringComparer.Ordinal);

        var buildResult = new StoryContextBuilder(characters, worlds).Build(
            new StoryContextRequest { CreativeDirection = direction, StoryPlan = grounded });

        await WriteJsonAsync(Path.Combine(caseDirectory, "story-context.json"), buildResult.Context);

        var contextCharacterIds = buildResult.Context.Characters.Select(character => character.Id.Value).ToList();
        var contextWorldIds = buildResult.Context.Worlds.Select(world => world.Id.Value).ToList();

        var resolves = buildResult.IsValid
            && contextCharacterIds.ToHashSet(StringComparer.Ordinal).SetEquals(referencedCharacters)
            && contextWorldIds.ToHashSet(StringComparer.Ordinal).SetEquals(referencedWorlds)
            && contextCharacterIds.Count == contextCharacterIds.Distinct(StringComparer.Ordinal).Count()
            && contextWorldIds.Count == contextWorldIds.Distinct(StringComparer.Ordinal).Count();

        storyContext = resolves
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
            };
    }

    var schemaValid = true;
    var characterIdsValid = proposal.CharacterBibles.All(bible => StoryIdentifier.IsValid(bible.Id.Value));
    var worldIdsValid = proposal.WorldBibles.All(bible => StoryIdentifier.IsValid(bible.Id.Value));
    var characterIdsUnique = characterIds.Distinct(StringComparer.Ordinal).Count() == characterIds.Count;
    var worldIdsUnique = worldIds.Distinct(StringComparer.Ordinal).Count() == worldIds.Count;

    var objectiveValid = jsonValid
        && schemaValid
        && characterIdsValid
        && worldIdsValid
        && characterIdsUnique
        && worldIdsUnique
        && grounding.RelationshipsValid
        && grounding.BeatGroundingsValid
        && grounding.CharacterRefsResolve
        && grounding.WorldRefsResolve
        && leaks.Count == 0
        && assetReferences.Status != "FAIL"
        && groundApply.Status == "PASS"
        && storyContext.Status == "PASS"
        && immutability.Status == "PASS";

    var reviewSignals = characterReuse.Status != "PASS"
        || worldReuse.Status != "PASS"
        || fragmentation.Count > 0
        || identityQuality.Count > 0
        || worldIdentityQuality.Count > 0
        || assetReferences.Status == "REVIEW";

    var report = new CaseReport
    {
        Case = label,
        Name = setup.Name,
        JsonValid = jsonValid,
        SchemaValid = schemaValid,
        CharacterCount = proposal.CharacterBibles.Count,
        WorldCount = proposal.WorldBibles.Count,
        CharacterIdsValid = characterIdsValid,
        WorldIdsValid = worldIdsValid,
        CharacterIdsUnique = characterIdsUnique,
        WorldIdsUnique = worldIdsUnique,
        RelationshipsValid = grounding.RelationshipsValid,
        BeatGroundingsValid = grounding.BeatGroundingsValid,
        CharacterRefsResolve = grounding.CharacterRefsResolve,
        WorldRefsResolve = grounding.WorldRefsResolve,
        CharacterReuse = characterReuse,
        WorldReuse = worldReuse,
        IdentityFragmentation = fragmentation.Count == 0
            ? new SignalStatus { Status = "PASS" }
            : new SignalStatus { Status = "REVIEW", Details = fragmentation },
        IdentityQuality = identityQuality.Count == 0
            ? new SignalStatus { Status = "PASS" }
            : new SignalStatus { Status = "REVIEW", Details = identityQuality },
        WorldIdentityQuality = worldIdentityQuality.Count == 0
            ? new SignalStatus { Status = "PASS" }
            : new SignalStatus { Status = "REVIEW", Details = worldIdentityQuality },
        AssetReferences = assetReferences,
        ImplementationLeaks = leaks,
        GroundApply = groundApply,
        StoryContext = storyContext,
        Immutability = immutability,
        OutputChars = rawResponse.Length,
        OutputTokens = recorder.LastResponse?.OutputTokenCount,
        LatencyMs = stopwatch.ElapsedMilliseconds,
        ProviderDurationMs = recorder.LastResponse?.TotalDuration?.TotalMilliseconds,
        Result = ResultPolicy.Compute(objectiveValid, reviewSignals),
        ErrorCode = error is StoryBiblePlanningException planningError
            ? planningError.Code
            : error is StoryPlanGroundingException groundingError
                ? groundingError.Code
                : error?.GetType().Name,
        ErrorMessage = error?.Message,
        Characters = proposal.CharacterBibles
            .Select(bible => new CharacterPreview
            {
                Id = bible.Id.Value,
                DisplayName = bible.DisplayName,
                Role = bible.Identity?.Role ?? string.Empty,
                Species = bible.Identity?.Species,
                AgePresentation = bible.Identity?.AgePresentation,
                PersonalityTraits = bible.PersonalityTraits,
                VisualDescription = bible.Identity?.VisualDescription
            })
            .ToList(),
        Worlds = proposal.WorldBibles
            .Select(bible => new WorldPreview
            {
                Id = bible.Id.Value,
                DisplayName = bible.DisplayName,
                EnvironmentType = bible.Identity?.EnvironmentType ?? string.Empty,
                RecurringProps = bible.RecurringProps,
                VisualDescription = bible.Identity?.VisualDescription ?? string.Empty
            })
            .ToList(),
        Groundings = proposal.BeatGroundings
            .Select(beatGrounding => new GroundingPreview
            {
                BeatId = beatGrounding.BeatId.Value,
                CharacterRefs = beatGrounding.CharacterRefs,
                WorldRefs = beatGrounding.WorldRefs
            })
            .ToList()
    };

    await WriteJsonAsync(Path.Combine(caseDirectory, "validation.json"), report);

    return report;
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
