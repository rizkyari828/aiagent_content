using System.Diagnostics;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Creative;
using AIStudio.Application.Rendering;
using AIStudio.Application.Stories;
using AIStudio.Infrastructure;
using AIStudio.Infrastructure.AI;
using AIStudio.StoryValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Real-Qwen Story Director validation harness. It feeds the five real
// CreativeDirection artifacts into the PRODUCTION Qwen-backed Story Director
// (resolved through StoryDirector:Mode=qwen), preserves raw artifacts, and prints a
// concise report. It never starts production, a database, or a media pipeline.
const string Usage =
    """
    Story Director Real-Qwen Validation

    Usage:
      dotnet <harness>.dll --directions <creative-direction-artifacts-dir> --output <dir>
      dotnet <harness>.dll --self-check

    Configuration is read from the same environment keys the API uses:
      Ollama__BaseUrl                 default http://127.0.0.1:11434
      Ollama__DefaultModel            required; the configured local model
      Ollama__TimeoutSeconds          default 300 (1..600)
      StoryDirector__Mode             must be qwen for this harness
      AISTUDIO_VALIDATION_OUTPUT      artifact directory override
      AISTUDIO_CREATIVE_DIRECTIONS    creative-direction artifacts directory override
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
        "Ollama__DefaultModel is not set. Source the local .env or run through scripts/e2e-story-director-qwen.sh.");
    return 2;
}

var baseUrl = GetEnv("Ollama__BaseUrl") ?? "http://127.0.0.1:11434";
var timeoutSeconds = GetEnvInt("Ollama__TimeoutSeconds", 300, 1, 600);
var requestedMode = GetEnv("StoryDirector__Mode") ?? StoryDirectorOptions.QwenMode;

var directionsDirectory = GetOption(args, "--directions")
    ?? GetEnv("AISTUDIO_CREATIVE_DIRECTIONS");
if (string.IsNullOrWhiteSpace(directionsDirectory) || !Directory.Exists(directionsDirectory))
{
    Console.Error.WriteLine(
        $"CreativeDirection artifacts directory not found: '{directionsDirectory}'. "
        + "Run the Creative Director real-Qwen validation first, or set AISTUDIO_CREATIVE_DIRECTIONS.");
    return 2;
}

var outputDirectory = GetOption(args, "--output")
    ?? GetEnv("AISTUDIO_VALIDATION_OUTPUT")
    ?? Path.Combine(
        Directory.GetCurrentDirectory(),
        "artifacts",
        $"story-director-qwen-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}");

var settings = new Dictionary<string, string?>
{
    ["ConnectionStrings:DefaultConnection"] =
        "Host=127.0.0.1;Database=aistudio_validation;Username=aistudio;Password=not-used",
    ["Ai:Provider"] = "Ollama",
    ["Ollama:BaseUrl"] = baseUrl,
    ["Ollama:DefaultModel"] = model,
    ["Ollama:TimeoutSeconds"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
    ["GpuResourceGate:Enabled"] = "true",
    ["StoryDirector:Mode"] = requestedMode,
    ["JobWorker:Enabled"] = "false",
    ["JobWorker:PollInterval"] = "00:00:01",
    ["JobWorker:LeaseDuration"] = "00:02:00",
    ["Assets:RootPath"] = "."
};

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(settings)
    .Build();

var services = new ServiceCollection();
services.AddLogging();
services.AddInfrastructure(configuration);

// Observe the real provider without duplicating any Story Director behavior: the
// production QwenStoryDirector still resolves IAiTextGenerator through DI.
services.AddSingleton<IAiTextGenerator>(serviceProvider =>
{
    var ollamaOptions = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>();
    var options = ollamaOptions.Value;
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/", UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
    };

    var inner = new OllamaTextGenerator(
        httpClient,
        ollamaOptions,
        serviceProvider.GetRequiredService<IGpuResourceGate>(),
        NullLogger<OllamaTextGenerator>.Instance);

    return new RecordingAiTextGenerator(inner);
});

using var provider = services.BuildServiceProvider();

var director = provider.GetRequiredService<IStoryDirector>();
if (director is not QwenStoryDirector)
{
    Console.Error.WriteLine(
        $"Expected the production QwenStoryDirector but resolved {director.GetType().Name}. "
        + $"Set StoryDirector__Mode={StoryDirectorOptions.QwenMode}.");
    return 2;
}

var patterns = provider.GetRequiredService<INarrativePatternRegistry>();
var recorder = (RecordingAiTextGenerator)provider.GetRequiredService<IAiTextGenerator>();

var startedAt = DateTimeOffset.UtcNow;
Directory.CreateDirectory(outputDirectory);

Console.WriteLine("Story Director Real-Qwen Validation");
Console.WriteLine($"  mode:     {requestedMode}");
Console.WriteLine($"  model:    {model}");
Console.WriteLine($"  base-url: {baseUrl}");
Console.WriteLine($"  timeout:  {timeoutSeconds}s");
Console.WriteLine($"  input:    {directionsDirectory}");
Console.WriteLine($"  output:   {outputDirectory}");
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
        Console.WriteLine($"Case {index + 1}: {setup.Name}");

        var report = await RunCaseAsync(index, setup, cancellation.Token);
        reports.Add(report);
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
    Mode = requestedMode,
    Model = model,
    BaseUrl = baseUrl,
    TimeoutSeconds = timeoutSeconds,
    DirectionsDir = directionsDirectory,
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
Console.WriteLine("Artifacts:");
Console.WriteLine(outputDirectory);

// A run where every case hard-failed is treated as a fatal validation failure;
// individual FAIL/REVIEW findings still exit zero for human review.
return fail == reports.Count && reports.Count > 0 ? 1 : 0;

async Task<CaseReport> RunCaseAsync(
    int index,
    ValidationCaseSetup setup,
    CancellationToken cancellationToken)
{
    var caseDirectory = Path.Combine(outputDirectory, $"case-{index + 1}-{setup.Name}");
    Directory.CreateDirectory(caseDirectory);

    var directionPath = Path.Combine(directionsDirectory, setup.DirectoryName, "creative-direction.json");
    var directionJson = await File.ReadAllTextAsync(directionPath, cancellationToken);
    var direction = CreativeDirectionParser.Parse(directionJson);
    await File.WriteAllTextAsync(
        Path.Combine(caseDirectory, "creative-direction.json"),
        directionJson,
        cancellationToken);

    if (!patterns.TryGetLatest(new NarrativePatternId(setup.PatternId), out var pattern))
    {
        throw new InvalidOperationException(
            $"Harness error: narrative pattern '{setup.PatternId}' is not registered.");
    }

    await WriteJsonAsync(Path.Combine(caseDirectory, "narrative-pattern.json"), pattern);

    var request = new StoryDirectorRequest
    {
        CreativeDirection = direction,
        NarrativePattern = new NarrativePatternId(setup.PatternId)
    };

    recorder.Reset();
    var stopwatch = Stopwatch.StartNew();
    StoryPlan? plan = null;
    Exception? error = null;

    try
    {
        plan = director.Direct(request).Plan;
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        error = exception;
    }

    stopwatch.Stop();

    var request2 = recorder.LastRequest;
    if (request2 is not null)
    {
        var prompt = $"[system]{Environment.NewLine}{request2.SystemPrompt}"
            + $"{Environment.NewLine}{Environment.NewLine}[user]{Environment.NewLine}{request2.Prompt}";
        await File.WriteAllTextAsync(Path.Combine(caseDirectory, "prompt.txt"), prompt, cancellationToken);
    }

    var rawResponse = recorder.LastResponse?.Text ?? string.Empty;
    await File.WriteAllTextAsync(
        Path.Combine(caseDirectory, "raw-response.txt"),
        rawResponse,
        cancellationToken);

    var jsonValid = ValidationOutput.TryParseJsonObject(rawResponse);
    var schemaValid = plan is not null;

    if (plan is not null)
    {
        await WriteJsonAsync(Path.Combine(caseDirectory, "story-plan.json"), plan);
    }

    var requestedPatternId = new NarrativePatternId(setup.PatternId);
    var patternKnown = plan is not null
        && plan.NarrativePattern == requestedPatternId
        && plan.NarrativePatternVersion == pattern.Version;
    var patternStatus = plan is null ? "unavailable" : patternKnown ? "Known" : "Mismatch";

    var conceptPreserved = plan is not null && plan.SourceConceptId == direction.Concept.Id;

    IReadOnlyList<StoryBeat> beats = plan?.Beats ?? [];
    var planText = plan is null
        ? string.Empty
        : string.Join(' ', beats.Select(beat => $"{beat.Role.Value} {beat.Purpose}"));

    IReadOnlyList<string> scriptHits = plan is null ? [] : ScriptBoundaryScanner.Scan(planText);
    IReadOnlyList<string> storyboardHits = plan is null ? [] : StoryboardBoundaryScanner.Scan(planText);
    IReadOnlyList<string> leaks = plan is null ? [] : ImplementationLeakScanner.Scan(rawResponse);

    var report = new CaseReport
    {
        Case = $"case-{index + 1}-{setup.Name}",
        Name = setup.Name,
        ExpectedPatternId = setup.PatternId,
        PatternId = plan?.NarrativePattern.Value ?? string.Empty,
        PatternVersion = plan?.NarrativePatternVersion.Value ?? 0,
        PatternStatus = patternStatus,
        JsonValid = jsonValid,
        SchemaValid = schemaValid,
        ConceptPreserved = conceptPreserved,
        ExpectedConceptId = direction.Concept.Id.Value,
        SourceConceptId = plan?.SourceConceptId.Value ?? string.Empty,
        BeatCount = beats.Count,
        BeatOrderValid = schemaValid,
        DurationTargetSeconds = plan?.TargetDurationSeconds ?? 0,
        DurationSumSeconds = beats.Sum(beat => beat.TargetDurationSeconds),
        DurationValid = schemaValid,
        ContinuityValid = schemaValid,
        CharacterWorldRefsValid = schemaValid,
        ScriptBoundaryHits = scriptHits,
        StoryboardBoundaryHits = storyboardHits,
        ImplementationLeaks = leaks,
        OutputChars = rawResponse.Length,
        OutputTokens = recorder.LastResponse?.OutputTokenCount,
        LatencyMs = stopwatch.ElapsedMilliseconds,
        ProviderDurationMs = recorder.LastResponse?.TotalDuration?.TotalMilliseconds,
        Narrative = plan is null ? "unavailable" : "REVIEW",
        Result = ResultPolicy.Compute(
            jsonValid,
            schemaValid,
            conceptPreserved,
            patternKnown,
            leaks.Count > 0,
            scriptHits.Count > 0,
            storyboardHits.Count > 0),
        ErrorCode = error switch
        {
            StoryPlanException story => story.Code,
            StoryDirectorException directorError => directorError.Code,
            AiGenerationException ai => ai.ErrorCode.ToString(),
            null => null,
            _ => error?.GetType().Name
        },
        ErrorMessage = error?.Message,
        Beats = beats
            .OrderBy(beat => beat.Order)
            .Select(beat => new BeatSummary
            {
                Order = beat.Order,
                Role = beat.Role.Value,
                Purpose = beat.Purpose,
                TargetDurationSeconds = beat.TargetDurationSeconds,
                ContinuityFrom = beat.ContinuityFrom.Select(reference => reference.Value).ToList()
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
    if (raw is null || !int.TryParse(raw, out var value))
    {
        return fallback;
    }

    return Math.Clamp(value, minimum, maximum);
}
