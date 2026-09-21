using System.Diagnostics;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Capabilities;
using AIStudio.Application.Creative;
using AIStudio.Application.ProductionRecipes;
using AIStudio.CreativeValidation;
using AIStudio.Infrastructure.AI;
using AIStudio.Infrastructure.Gpu;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Real-Qwen Creative Director validation harness. It drives the production
// ICreativeDirector + OllamaTextGenerator once per approved-idea case, verifies
// the deterministic dimensions, preserves raw artifacts, and prints a concise
// report. It never starts production, a database, or a media pipeline.
const string Usage =
    """
    Creative Director Real-Qwen Validation

    Usage:
      dotnet <harness>.dll --output <dir>
      dotnet <harness>.dll --self-check

    Configuration is read from the same environment keys the API uses:
      Ollama__BaseUrl          default http://127.0.0.1:11434
      Ollama__DefaultModel     required; the configured local model
      Ollama__TimeoutSeconds   default 300 (1..600)
      GpuResourceGate__Enabled default true
      SpeechSynthesis__Enabled / MusicGeneration__Enabled / Manim__Enabled /
      ComfyUi__Enabled / Blender__Enabled   capability availability (default false)
      AISTUDIO_VALIDATION_OUTPUT   artifact directory override
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
        "Ollama__DefaultModel is not set. Source the local .env or run through scripts/e2e-creative-director-qwen.sh.");
    return 2;
}

var baseUrl = GetEnv("Ollama__BaseUrl") ?? "http://127.0.0.1:11434";
var timeoutSeconds = GetEnvInt("Ollama__TimeoutSeconds", 300, 1, 600);
var outputDir = GetOption(args, "--output")
    ?? GetEnv("AISTUDIO_VALIDATION_OUTPUT")
    ?? Path.Combine(
        Directory.GetCurrentDirectory(),
        "artifacts",
        $"creative-director-qwen-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}");

var capabilities = new CapabilityRegistry(
    ProductionCapabilityCatalog.Capabilities,
    ProductionCapabilityCatalog.CreateProviders(
        speechEnabled: GetEnvBool("SpeechSynthesis__Enabled", false),
        musicEnabled: GetEnvBool("MusicGeneration__Enabled", false),
        manimEnabled: GetEnvBool("Manim__Enabled", false),
        imageEnabled: GetEnvBool("ComfyUi__Enabled", false),
        threeDEnabled: GetEnvBool("Blender__Enabled", false)));

var recipeRegistry = new ProductionRecipeRegistry(SeedProductionRecipes.All);
var recipeResolver = new ProductionRecipeResolver(capabilities);
var planningContextProvider = new CreativePlanningContextProvider(
    recipeRegistry,
    recipeResolver,
    capabilities);
var planningContext = planningContextProvider.Build();

using var httpClient = new HttpClient
{
    BaseAddress = new Uri($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute),
    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
};

var gpuGate = new GpuResourceGate(
    Options.Create(new GpuResourceGateOptions
    {
        Enabled = GetEnvBool("GpuResourceGate__Enabled", true)
    }),
    NullLogger<GpuResourceGate>.Instance);

var provider = new OllamaTextGenerator(
    httpClient,
    Options.Create(new OllamaOptions
    {
        BaseUrl = baseUrl,
        DefaultModel = model,
        TimeoutSeconds = timeoutSeconds
    }),
    gpuGate,
    NullLogger<OllamaTextGenerator>.Instance);

var recorder = new RecordingAiTextGenerator(provider);
ICreativeDirector director = new CreativeDirector(recorder, planningContextProvider);

var startedAt = DateTimeOffset.UtcNow;
Directory.CreateDirectory(outputDir);

Console.WriteLine("Creative Director Real-Qwen Validation");
Console.WriteLine($"  model:    {model}");
Console.WriteLine($"  base-url: {baseUrl}");
Console.WriteLine($"  timeout:  {timeoutSeconds}s");
Console.WriteLine($"  output:   {outputDir}");
Console.WriteLine(
    $"  capabilities: available=[{string.Join(", ", planningContext.AvailableCapabilities)}] "
    + $"unavailable=[{string.Join(", ", planningContext.UnavailableCapabilities)}]");
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
    for (var index = 0; index < ValidationCases.All.Count; index++)
    {
        var validationCase = ValidationCases.All[index];
        Console.WriteLine($"Case {index + 1}: {validationCase.Name}");

        var report = await RunCaseAsync(index, validationCase, cancellation.Token);
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
    Model = model,
    BaseUrl = baseUrl,
    TimeoutSeconds = timeoutSeconds,
    PlanningContext = planningContext,
    Pass = pass,
    Review = review,
    Fail = fail,
    Overall = overall,
    Cases = reports
};

await WriteJsonAsync(Path.Combine(outputDir, "summary.json"), summary);

Console.WriteLine("Summary");
Console.WriteLine($"PASS: {pass}");
Console.WriteLine($"REVIEW: {review}");
Console.WriteLine($"FAIL: {fail}");
Console.WriteLine();
Console.WriteLine("Artifacts:");
Console.WriteLine(outputDir);

// A run where every case hard-failed is treated as a fatal validation failure;
// individual FAIL/REVIEW findings still exit zero for human review.
return fail == reports.Count && reports.Count > 0 ? 1 : 0;

async Task<CaseReport> RunCaseAsync(
    int index,
    ValidationCase validationCase,
    CancellationToken cancellationToken)
{
    var idea = validationCase.Idea;
    var caseDirectory = Path.Combine(outputDir, $"case-{index + 1}-{validationCase.Name}");
    Directory.CreateDirectory(caseDirectory);

    await WriteJsonAsync(Path.Combine(caseDirectory, "approved-idea.json"), idea);

    var stopwatch = Stopwatch.StartNew();
    CreativeDirectionResult? result = null;
    CreativeDirectionException? directionError = null;
    AiGenerationException? aiError = null;
    Exception? otherError = null;

    try
    {
        result = await director.DirectAsync(
            idea,
            new CreativeDirectionOptions(),
            cancellationToken);
    }
    catch (CreativeDirectionException exception)
    {
        directionError = exception;
    }
    catch (AiGenerationException exception)
    {
        aiError = exception;
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        otherError = exception;
    }

    stopwatch.Stop();

    var rawResponse = recorder.LastResponse?.Text ?? string.Empty;
    var request = recorder.LastRequest;

    if (request is not null)
    {
        var prompt = $"[system]{Environment.NewLine}{request.SystemPrompt}"
            + $"{Environment.NewLine}{Environment.NewLine}[user]{Environment.NewLine}{request.Prompt}";
        await File.WriteAllTextAsync(
            Path.Combine(caseDirectory, "prompt.txt"),
            prompt,
            cancellationToken);
    }

    await File.WriteAllTextAsync(
        Path.Combine(caseDirectory, "raw-response.txt"),
        rawResponse,
        cancellationToken);

    var jsonValid = ValidationOutput.TryParseJsonObject(rawResponse);
    var schemaValid = result is not null;
    var direction = result?.Direction;

    CreativeDirection? parsedRaw = null;
    if (jsonValid)
    {
        try
        {
            parsedRaw = CreativeDirectionParser.Parse(rawResponse);
            schemaValid |= direction is null;
            direction ??= parsedRaw;
        }
        catch (CreativeDirectionException)
        {
            // Parsing the raw response failed for the same reason the director
            // rejected it; schemaValid stays false.
        }
    }

    if (direction is not null)
    {
        await WriteJsonAsync(
            Path.Combine(caseDirectory, "creative-direction.json"),
            direction);
    }

    var preservation = direction is null
        ? new IdeaPreservation(0, false, [], [])
        : IdeaTextMatcher.Check($"{idea.Topic} {idea.Angle}", ValidationOutput.DirectionText(direction));

    var recipeCheck = direction is null
        ? new RecipeCheck(string.Empty, string.Empty, "Unknown", [])
        : RecipeLookup.Classify(direction, recipeRegistry, recipeResolver);

    var audiencePreserved = direction is not null
        && string.Equals(
            direction.Concept.Audience.Trim(),
            idea.Audience.Trim(),
            StringComparison.Ordinal);

    var audienceFromModel = parsedRaw?.Concept.Audience;
    var audienceReplaced = parsedRaw is not null
        && !string.Equals(
            audienceFromModel?.Trim(),
            idea.Audience.Trim(),
            StringComparison.Ordinal);

    var leaks = ImplementationLeakScanner.Scan(rawResponse);

    var report = new CaseReport
    {
        Case = validationCase.Idea.IdeaReference ?? $"case-{index + 1}",
        Name = validationCase.Name,
        JsonValid = jsonValid,
        SchemaValid = schemaValid,
        IdeaPreserved = preservation.Preserved,
        IdeaCoverage = Math.Round(preservation.Coverage, 3),
        IdeaMissingTokens = preservation.Missing,
        AudiencePreserved = audiencePreserved,
        AudienceFromModel = audienceFromModel,
        AudienceReplacedByModel = audienceReplaced,
        RecipeId = recipeCheck.Id,
        RecipeVersion = recipeCheck.Version,
        RecipeStatus = recipeCheck.Status,
        MissingCapabilities = recipeCheck.MissingCapabilities,
        Format = direction?.Concept.Format ?? string.Empty,
        Style = direction?.Concept.Style ?? string.Empty,
        TreatmentUsefulness = direction is null ? "N/A" : "REVIEW",
        Treatment = direction is null ? null : new TreatmentArtifact
        {
            StoryApproach = direction.Treatment.StoryApproach,
            HookTreatment = direction.Treatment.HookTreatment,
            Pacing = direction.Treatment.Pacing,
            VisualStrategy = direction.Treatment.VisualStrategy,
            EndingTreatment = direction.Treatment.EndingTreatment,
            Tone = direction.Treatment.Tone,
            TransitionStrategy = direction.Treatment.TransitionStrategy
        },
        ImplementationLeaks = leaks,
        OutputChars = rawResponse.Length,
        OutputTokens = recorder.LastResponse?.OutputTokenCount,
        LatencyMs = stopwatch.ElapsedMilliseconds,
        ProviderDurationMs = recorder.LastResponse?.TotalDuration?.TotalMilliseconds,
        Result = ResultPolicy.Compute(
            jsonValid,
            schemaValid,
            preservation.Preserved,
            audiencePreserved,
            leaks.Count > 0,
            recipeCheck.Status),
        ErrorCode = directionError?.Code
            ?? aiError?.ErrorCode.ToString()
            ?? (otherError is null ? null : otherError.GetType().Name),
        ErrorMessage = directionError?.Message ?? aiError?.Message ?? otherError?.Message
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

static bool GetEnvBool(string name, bool fallback)
{
    var raw = GetEnv(name);
    return raw is null
        ? fallback
        : raw.ToLowerInvariant() is "1" or "true" or "yes" or "on";
}
