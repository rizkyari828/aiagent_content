using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Creative;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Stories;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;
using AIStudio.Infrastructure.AI;
using AIStudio.Infrastructure.Gpu;
using AIStudio.StoryboardValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Real-Qwen Storyboard validation harness. It drives the PRODUCTION
// GenerateStoryboardJobHandler (real StoryContextBuilder + real
// GenerateStoryboardPrompt + real OllamaTextGenerator + real
// GenerateStoryboardResult) over the validated real CreativeDirection / StoryPlan /
// Script artifact triples, preserves raw artifacts, and prints a concise report.
const string Usage =
    """
    Storyboard Real-Qwen Validation

    Usage:
      dotnet <harness>.dll --creative <dir> --story <dir> --script <dir> --output <dir>
      dotnet <harness>.dll --self-check

    Configuration uses the same environment keys the API uses:
      Ollama__BaseUrl                 default http://127.0.0.1:11434
      Ollama__DefaultModel            required; the configured local model
      Ollama__TimeoutSeconds          default 300 (1..600)
      AISTUDIO_CREATIVE_DIRECTIONS    creative-direction artifacts directory override
      AISTUDIO_STORY_DIRECTIONS       story-plan artifacts directory override
      AISTUDIO_SCRIPT_DIRECTIONS      script-result artifacts directory override
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
        "Ollama__DefaultModel is not set. Source the local .env or run through scripts/e2e-storyboard-qwen.sh.");
    return 2;
}

var baseUrl = GetEnv("Ollama__BaseUrl") ?? "http://127.0.0.1:11434";
var timeoutSeconds = GetEnvInt("Ollama__TimeoutSeconds", 300, 1, 600);

var creativeDirectory = GetOption(args, "--creative") ?? GetEnv("AISTUDIO_CREATIVE_DIRECTIONS");
var storyDirectory = GetOption(args, "--story") ?? GetEnv("AISTUDIO_STORY_DIRECTIONS");
var scriptDirectory = GetOption(args, "--script") ?? GetEnv("AISTUDIO_SCRIPT_DIRECTIONS");

foreach (var (directory, name) in new[]
{
    (creativeDirectory, "CreativeDirection"),
    (storyDirectory, "StoryPlan"),
    (scriptDirectory, "Script")
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
var scriptRoot = scriptDirectory!;

var outputDirectory = GetOption(args, "--output")
    ?? GetEnv("AISTUDIO_VALIDATION_OUTPUT")
    ?? Path.Combine(
        Directory.GetCurrentDirectory(),
        "artifacts",
        $"storyboard-qwen-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}");

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

Console.WriteLine("Storyboard Real-Qwen Validation");
Console.WriteLine($"  model:     {model}");
Console.WriteLine($"  base-url:  {baseUrl}");
Console.WriteLine($"  timeout:   {timeoutSeconds}s");
Console.WriteLine($"  creative:  {creativeDirectory}");
Console.WriteLine($"  story:     {storyDirectory}");
Console.WriteLine($"  script:    {scriptDirectory}");
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
    CreativeDirectionDir = creativeRoot,
    StoryPlanDir = storyRoot,
    ScriptDir = scriptRoot,
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

return fail == reports.Count && reports.Count > 0 ? 1 : 0;

async Task<(CaseReport Report, bool GroundingExercised)> RunCaseAsync(
    int index,
    ValidationCaseSetup setup,
    CancellationToken cancellationToken)
{
    var caseDirectory = Path.Combine(outputDirectory, $"case-{index + 1}-{setup.Name}");
    Directory.CreateDirectory(caseDirectory);

    var directionJson = await File.ReadAllTextAsync(
        Path.Combine(creativeRoot, setup.DirectoryName, "creative-direction.json"),
        cancellationToken);
    var storyJson = await File.ReadAllTextAsync(
        Path.Combine(storyRoot, setup.DirectoryName, "story-plan.json"),
        cancellationToken);
    var scriptJson = await File.ReadAllTextAsync(
        Path.Combine(scriptRoot, setup.DirectoryName, "script-result.json"),
        cancellationToken);

    var direction = CreativeDirectionParser.Parse(directionJson);
    var plan = StoryPlanParser.Parse(storyJson, patternRegistry);
    var script = GenerateScriptResult.Deserialize(scriptJson);
    var scriptContent = script.Serialize();

    await File.WriteAllTextAsync(Path.Combine(caseDirectory, "creative-direction.json"), directionJson, cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(caseDirectory, "story-plan.json"), storyJson, cancellationToken);
    await File.WriteAllTextAsync(Path.Combine(caseDirectory, "script-result.json"), scriptJson, cancellationToken);

    var selectedIdeaPath = Path.Combine(scriptRoot, setup.DirectoryName, "selected-idea.json");
    if (File.Exists(selectedIdeaPath))
    {
        await File.WriteAllTextAsync(
            Path.Combine(caseDirectory, "selected-idea.json"),
            await File.ReadAllTextAsync(selectedIdeaPath, cancellationToken),
            cancellationToken);
    }

    var projectId = Guid.NewGuid();
    var createdAt = DateTimeOffset.UtcNow;
    var reviewedScript = ReviewedScript.Create(projectId, Guid.NewGuid(), scriptContent, createdAt);
    reviewedScript.Approve(createdAt.AddMinutes(1));

    var payload = new GenerateStoryboardJobPayload(projectId, direction, plan);
    var payloadJson = JsonSerializer.Serialize(payload, ValidationArtifacts.Json);

    var aiRecorder = new RecordingAiTextGenerator(provider);
    var contextRecorder = new RecordingStoryContextBuilder(StoryContextFactory.Create());
    var handler = new GenerateStoryboardJobHandler(
        new StaticContentProjectReader(
            new ContentProjectSnapshot(
                projectId,
                $"Storyboard validation: {setup.Name}",
                direction.Concept.Description)),
        new StaticScriptReviewRepository(reviewedScript),
        aiRecorder,
        contextRecorder);

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
                JobType.GenerateStoryboard,
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
    await File.WriteAllTextAsync(Path.Combine(caseDirectory, "raw-response.txt"), rawResponse, cancellationToken);

    if (contextRecorder.LastResult is { } contextResult)
    {
        await WriteJsonAsync(Path.Combine(caseDirectory, "story-context.json"), contextResult.Context);
    }

    GenerateStoryboardResult? storyboard = null;
    if (resultJson is not null)
    {
        try
        {
            storyboard = GenerateStoryboardResult.Deserialize(resultJson);
        }
        catch (Exception)
        {
            storyboard = null;
        }

        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "storyboard-result.json"), resultJson, cancellationToken);
    }

    var jsonValid = ValidationOutput.TryParseJsonObject(rawResponse);
    var schemaValid = storyboard is not null;

    var sceneText = storyboard is null
        ? string.Empty
        : string.Join(
            ' ',
            storyboard.Scenes.Select(scene => $"{scene.Heading} {scene.Visual}"));

    IReadOnlyList<string> rewriteHits = storyboard is null ? [] : ScriptRewriteScanner.Scan(sceneText);
    IReadOnlyList<string> leaks = storyboard is null ? [] : ImplementationLeakScanner.Scan(sceneText);

    var orderedBeats = plan.Beats.OrderBy(beat => beat.Order).ToList();
    var sections = script.Sections;

    var report = new CaseReport
    {
        Case = $"case-{index + 1}-{setup.Name}",
        Name = setup.Name,
        JsonValid = jsonValid,
        SchemaValid = schemaValid,
        TitleValid = storyboard is not null && !string.IsNullOrWhiteSpace(storyboard.Title),
        BeatCount = orderedBeats.Count,
        SectionCount = sections.Count,
        SceneCount = storyboard?.Scenes.Count ?? 0,
        ScriptRewriteHits = rewriteHits,
        ImplementationLeaks = leaks,
        OutputChars = rawResponse.Length,
        OutputTokens = aiRecorder.LastResponse?.OutputTokenCount,
        LatencyMs = stopwatch.ElapsedMilliseconds,
        ProviderDurationMs = aiRecorder.LastResponse?.TotalDuration?.TotalMilliseconds,
        StoryAlignment = schemaValid ? "REVIEW" : "unavailable",
        ScriptAlignment = schemaValid ? "REVIEW" : "unavailable",
        VisualQuality = schemaValid ? "REVIEW" : "unavailable",
        Result = ResultPolicy.Compute(jsonValid, schemaValid, leaks.Count > 0, rewriteHits.Count),
        ErrorCode = error switch
        {
            JobExecutionException job => job.ErrorCode,
            null => null,
            _ => error?.GetType().Name
        },
        ErrorMessage = error?.Message,
        Title = storyboard?.Title ?? string.Empty,
        Beats = orderedBeats
            .Select(beat => new BeatPreview
            {
                Order = beat.Order,
                Role = beat.Role.Value,
                Purpose = beat.Purpose
            })
            .ToList(),
        Sections = sections
            .Select((section, sectionIndex) => new SectionPreview
            {
                Index = sectionIndex + 1,
                Heading = section.Heading,
                NarrationPreview = ValidationOutput.Shorten(section.Narration, 160)
            })
            .ToList(),
        Scenes = storyboard is null
            ? []
            : storyboard.Scenes
                .Select((scene, sceneIndex) => new ScenePreview
                {
                    Index = sceneIndex + 1,
                    Heading = scene.Heading,
                    Visual = scene.Visual
                })
                .ToList()
    };

    await WriteJsonAsync(Path.Combine(caseDirectory, "validation.json"), report);

    var exercised = orderedBeats.Any(
        beat => beat.CharacterRefs.Count > 0 || beat.WorldRefs.Count > 0);

    return (report, exercised);
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
