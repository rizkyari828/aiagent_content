using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Creative;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Stories;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.AI;
using AIStudio.Infrastructure.Gpu;
using AIStudio.ScriptValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Real-Qwen Script Generation validation harness. It drives the PRODUCTION
// GenerateScriptJobHandler (real StoryContextBuilder + real GenerateScriptPrompt +
// real OllamaTextGenerator + real GenerateScriptResult) over the five validated
// CreativeDirection/StoryPlan artifact pairs, preserves raw artifacts, and prints a
// concise report. No database, job queue, or media pipeline is involved.
const string Usage =
    """
    Script Real-Qwen Validation

    Usage:
      dotnet <harness>.dll --creative <dir> --story <dir> --output <dir>
      dotnet <harness>.dll --self-check

    Configuration uses the same environment keys the API uses:
      Ollama__BaseUrl                 default http://127.0.0.1:11434
      Ollama__DefaultModel            required; the configured local model
      Ollama__TimeoutSeconds          default 300 (1..600)
      AISTUDIO_SCRIPT_LANGUAGE        default English
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
        "Ollama__DefaultModel is not set. Source the local .env or run through scripts/e2e-script-qwen.sh.");
    return 2;
}

var baseUrl = GetEnv("Ollama__BaseUrl") ?? "http://127.0.0.1:11434";
var timeoutSeconds = GetEnvInt("Ollama__TimeoutSeconds", 300, 1, 600);
var language = GetOption(args, "--language") ?? GetEnv("AISTUDIO_SCRIPT_LANGUAGE") ?? "English";

var creativeDirectory = GetOption(args, "--creative") ?? GetEnv("AISTUDIO_CREATIVE_DIRECTIONS");
var storyDirectory = GetOption(args, "--story") ?? GetEnv("AISTUDIO_STORY_DIRECTIONS");
if (string.IsNullOrWhiteSpace(creativeDirectory) || !Directory.Exists(creativeDirectory))
{
    Console.Error.WriteLine(
        $"CreativeDirection artifacts directory not found: '{creativeDirectory}'. Run the Creative Director validation first (or set AISTUDIO_CREATIVE_DIRECTIONS).");
    return 2;
}

if (string.IsNullOrWhiteSpace(storyDirectory) || !Directory.Exists(storyDirectory))
{
    Console.Error.WriteLine(
        $"StoryPlan artifacts directory not found: '{storyDirectory}'. Run the Story Director validation first (or set AISTUDIO_STORY_DIRECTIONS).");
    return 2;
}

var outputDirectory = GetOption(args, "--output")
    ?? GetEnv("AISTUDIO_VALIDATION_OUTPUT")
    ?? Path.Combine(
        Directory.GetCurrentDirectory(),
        "artifacts",
        $"script-qwen-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}");

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

Console.WriteLine("Script Real-Qwen Validation");
Console.WriteLine($"  model:     {model}");
Console.WriteLine($"  base-url:  {baseUrl}");
Console.WriteLine($"  timeout:   {timeoutSeconds}s");
Console.WriteLine($"  language:  {language}");
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
var groundingExercised = false;

try
{
    for (var index = 0; index < CaseCatalog.Cases.Count; index++)
    {
        var setup = CaseCatalog.Cases[index];
        var (report, exercised) = await RunCaseAsync(index, setup, cancellation.Token);
        reports.Add(report);
        groundingExercised |= exercised;

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
    Language = language,
    CreativeDirectionDir = creativeDirectory,
    StoryPlanDir = storyDirectory,
    CharacterWorldGroundingExercised = groundingExercised,
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
Console.WriteLine();
Console.WriteLine(
    groundingExercised
        ? "Character/World Bible grounding: EXERCISED (story-referenced bibles resolved)."
        : "Character/World Bible grounding: NOT EXERCISED in this run (the real StoryPlans carry no character/world refs).");
Console.WriteLine();
Console.WriteLine("Artifacts:");
Console.WriteLine(outputDirectory);

// A run where every case hard-failed is treated as a fatal validation failure;
// individual FAIL/REVIEW findings still exit zero for human review.
return fail == reports.Count && reports.Count > 0 ? 1 : 0;

async Task<(CaseReport Report, bool GroundingExercised)> RunCaseAsync(
    int index,
    ValidationCaseSetup setup,
    CancellationToken cancellationToken)
{
    var caseDirectory = Path.Combine(outputDirectory, $"case-{index + 1}-{setup.Name}");
    Directory.CreateDirectory(caseDirectory);

    var directionPath = Path.Combine(creativeDirectory, setup.DirectoryName, "creative-direction.json");
    var storyPath = Path.Combine(storyDirectory, setup.DirectoryName, "story-plan.json");
    var directionJson = await File.ReadAllTextAsync(directionPath, cancellationToken);
    var storyJson = await File.ReadAllTextAsync(storyPath, cancellationToken);

    var direction = CreativeDirectionParser.Parse(directionJson);
    var plan = StoryPlanParser.Parse(storyJson, patternRegistry);

    var idea = new GenerateIdeaResult(
        direction.Concept.Title,
        direction.Treatment.HookTreatment,
        direction.Concept.Description,
        direction.Treatment.StoryApproach,
        direction.Concept.Audience,
        direction.Concept.Format);

    await File.WriteAllTextAsync(
        Path.Combine(caseDirectory, "creative-direction.json"),
        directionJson,
        cancellationToken);
    await File.WriteAllTextAsync(
        Path.Combine(caseDirectory, "story-plan.json"),
        storyJson,
        cancellationToken);
    await WriteJsonAsync(Path.Combine(caseDirectory, "selected-idea.json"), idea);

    var projectId = Guid.NewGuid();
    var payload = new GenerateScriptJobPayload(
        projectId,
        idea,
        language,
        direction,
        plan);
    var payloadJson = JsonSerializer.Serialize(payload, ValidationArtifacts.Json);

    var reader = new StaticContentProjectReader(
        new ContentProjectSnapshot(
            projectId,
            $"Script validation: {setup.Name}",
            direction.Concept.Description));

    var aiRecorder = new RecordingAiTextGenerator(provider);
    var contextRecorder = new RecordingStoryContextBuilder(StoryContextFactory.Create());
    var handler = new GenerateScriptJobHandler(reader, aiRecorder, contextRecorder);

    aiRecorder.Reset();
    contextRecorder.Reset();

    var stopwatch = Stopwatch.StartNew();
    string? resultJson = null;
    Exception? error = null;

    try
    {
        resultJson = await handler.ExecuteAsync(
            new ClaimedJob(
                Guid.NewGuid(),
                projectId,
                JobType.GenerateScript,
                "input-v1",
                payloadJson,
                0,
                2,
                false),
            cancellationToken);
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        error = exception;
    }

    stopwatch.Stop();

    var request = aiRecorder.LastRequest;
    if (request is not null)
    {
        var prompt = $"[system]{Environment.NewLine}{request.SystemPrompt}"
            + $"{Environment.NewLine}{Environment.NewLine}[user]{Environment.NewLine}{request.Prompt}";
        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "prompt.txt"), prompt, cancellationToken);
    }

    var rawResponse = aiRecorder.LastResponse?.Text ?? string.Empty;
    await File.WriteAllTextAsync(
        Path.Combine(caseDirectory, "raw-response.txt"),
        rawResponse,
        cancellationToken);

    if (contextRecorder.LastResult is { } contextResult)
    {
        await WriteJsonAsync(Path.Combine(caseDirectory, "story-context.json"), contextResult.Context);
    }

    GenerateScriptResult? script = null;
    if (resultJson is not null)
    {
        try
        {
            script = GenerateScriptResult.Deserialize(resultJson);
        }
        catch (Exception)
        {
            script = null;
        }

        await File.WriteAllTextAsync(
            Path.Combine(caseDirectory, "script-result.json"),
            resultJson,
            cancellationToken);
    }

    var jsonValid = ValidationOutput.TryParseJsonObject(rawResponse);
    var schemaValid = script is not null;

    var scriptText = script is null
        ? string.Empty
        : BuildScriptText(script);
    var spokenText = script is null
        ? string.Empty
        : BuildSpokenText(script);

    var conceptCoverage = script is null
        ? 0d
        : Math.Round(
            TextOverlap.Coverage(
                $"{direction.Concept.Title} {direction.Concept.Description}",
                scriptText),
            3);
    var audienceCoverage = script is null
        ? 0d
        : Math.Round(TextOverlap.Coverage(direction.Concept.Audience, scriptText), 3);

    // Lexical overlap is a signal, not proof of semantic preservation: low overlap
    // is surfaced as REVIEW, never as an automatic hard failure.
    var conceptLowOverlap = conceptCoverage < ResultPolicy.MinimumConceptCoverage;
    var audienceLowOverlap = audienceCoverage <= 0;

    var beatCount = plan.Beats.Count;
    var sectionCount = script?.Sections.Count ?? 0;
    var sectionCountMatches = schemaValid && sectionCount == beatCount;

    IReadOnlyList<string> scriptBoundaryHits = script is null ? [] : ScriptBoundaryScanner.Scan(spokenText);
    IReadOnlyList<string> storyboardHits = script is null ? [] : StoryboardBoundaryScanner.Scan(spokenText);
    IReadOnlyList<string> leaks = script is null ? [] : ImplementationLeakScanner.Scan(rawResponse);

    var hookValid = script is not null
        && !string.IsNullOrWhiteSpace(script.OpeningHook)
        && ImplementationLeakScanner.Scan(script.OpeningHook).Count == 0
        && StoryboardBoundaryScanner.Scan(script.OpeningHook).Count == 0
        && ScriptBoundaryScanner.Scan(script.OpeningHook).Count == 0;
    var closingValid = script is not null && !string.IsNullOrWhiteSpace(script.Closing);

    var orderedBeats = plan.Beats.OrderBy(beat => beat.Order).ToList();
    var alignment = new List<BeatAlignment>(orderedBeats.Count);
    for (var beatIndex = 0; beatIndex < orderedBeats.Count; beatIndex++)
    {
        var beat = orderedBeats[beatIndex];
        var section = script is not null && beatIndex < script.Sections.Count
            ? script.Sections[beatIndex]
            : null;

        alignment.Add(new BeatAlignment
        {
            BeatOrder = beat.Order,
            BeatRole = beat.Role.Value,
            BeatPurpose = beat.Purpose,
            SectionIndex = section is null ? null : beatIndex + 1,
            SectionHeading = section?.Heading,
            NarrationPreview = section?.Narration
        });
    }

    var report = new CaseReport
    {
        Case = $"case-{index + 1}-{setup.Name}",
        Name = setup.Name,
        JsonValid = jsonValid,
        SchemaValid = schemaValid,
        ExpectedConceptId = direction.Concept.Id.Value,
        ConceptLexicalMatch = !conceptLowOverlap,
        ConceptCoverage = conceptCoverage,
        ExpectedAudience = direction.Concept.Audience,
        AudienceLexicalMatch = !audienceLowOverlap,
        AudienceCoverage = audienceCoverage,
        BeatCount = beatCount,
        SectionCount = sectionCount,
        SectionCountMatches = sectionCountMatches,
        OpeningHookValid = hookValid,
        ClosingValid = closingValid,
        ScriptBoundaryHits = scriptBoundaryHits,
        StoryboardBoundaryHits = storyboardHits,
        ImplementationLeaks = leaks,
        OutputChars = rawResponse.Length,
        OutputTokens = aiRecorder.LastResponse?.OutputTokenCount,
        LatencyMs = stopwatch.ElapsedMilliseconds,
        ProviderDurationMs = aiRecorder.LastResponse?.TotalDuration?.TotalMilliseconds,
        NarrativeFlow = schemaValid ? "REVIEW" : "unavailable",
        SpokenQuality = schemaValid ? "REVIEW" : "unavailable",
        Result = ResultPolicy.Compute(
            jsonValid,
            schemaValid,
            sectionCountMatches,
            conceptLowOverlap,
            audienceLowOverlap,
            leaks.Count > 0,
            scriptBoundaryHits.Count,
            storyboardHits.Count),
        ErrorCode = error switch
        {
            JobExecutionException job => job.ErrorCode,
            null => null,
            _ => error?.GetType().Name
        },
        ErrorMessage = error?.Message,
        Title = script?.Title ?? string.Empty,
        OpeningHook = script?.OpeningHook ?? string.Empty,
        Closing = script?.Closing ?? string.Empty,
        Alignment = alignment
    };

    await WriteJsonAsync(Path.Combine(caseDirectory, "validation.json"), report);

    var exercised = orderedBeats.Any(
        beat => beat.CharacterRefs.Count > 0 || beat.WorldRefs.Count > 0);

    return (report, exercised);
}

static string BuildScriptText(GenerateScriptResult script) =>
    string.Join(
        ' ',
        script.Title,
        script.OpeningHook,
        string.Join(' ', script.Sections.Select(section => $"{section.Heading} {section.Narration}")),
        script.Closing);

// Boundary checks examine only the spoken fields; section headings are metadata.
static string BuildSpokenText(GenerateScriptResult script) =>
    string.Join(
        ' ',
        script.OpeningHook,
        string.Join(' ', script.Sections.Select(section => section.Narration)),
        script.Closing);

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
