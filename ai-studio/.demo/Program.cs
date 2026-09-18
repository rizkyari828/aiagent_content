using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Domain.Assets;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: <v12|v2> <assets-root> [python] [script]");
    return 1;
}

var mode = args[0];
var assetsRoot = Path.GetFullPath(args[1]);

var rendering = new RenderingOptions
{
    Width = 1280,
    Height = 720,
    FrameRate = 30,
    EnableMotion = true,
    Transition = SceneTransition.Crossfade,
    TransitionDurationSeconds = 0.35,
    Subtitle = new SubtitleStyle(),
    BackgroundMusicPath = Path.Combine(assetsRoot, "vq1-demo/music_v1_1.wav"),
    BackgroundMusicVolume = 0.28,
    EnableDucking = true
};

var storage = new AssetStorageOptions { RootPath = assetsRoot };
var runner = new SystemProcessRunner();
var inspector = new FfprobeMediaInspector(Options.Create(rendering), runner);
var renderer = new FfmpegVideoRenderer(
    Options.Create(rendering),
    Options.Create(storage),
    runner,
    inspector);

var narration = Path.Combine(assetsRoot, "video1/narration.wav");
var subtitle = Path.Combine(assetsRoot, "video1/subtitle.srt");

if (mode == "v12")
{
    var scenes = new List<SceneMediaInput>();
    for (var index = 0; index < 8; index++)
    {
        scenes.Add(new SceneMediaInput(
            Path.Combine(assetsRoot, $"vq1-demo/visuals/scene_{index}.png"),
            AssetType.Image,
            VisualKind: SceneVisualKind.Graphic));
    }

    var output = await renderer.RenderAsync(
        new VideoRenderRequest(
            scenes,
            narration,
            "renders/vq1-demo/quality-v1.2.mp4",
            subtitle),
        CancellationToken.None);

    Console.WriteLine(
        $"mode={mode} path={output.RelativePath} bytes={output.ByteSize} hash={output.ContentHash} duration={output.DurationSeconds:0.###}");
    return 0;
}

if (args.Length < 4)
{
    Console.Error.WriteLine("v2 requires <python> <script>");
    return 1;
}

var storyboard = GenerateStoryboardResult.Deserialize(
    await File.ReadAllTextAsync(Path.Combine(assetsRoot, "vq2-demo/storyboard.json")));

var manimOptions = new ManimOptions
{
    Enabled = true,
    PythonPath = Path.GetFullPath(args[2]),
    ScriptPath = Path.GetFullPath(args[3]),
    TimeoutSeconds = 600
};

var svgRenderer = new FfmpegSceneVisualRenderer(Options.Create(rendering), runner);
var manimRenderer = new ProcessManimSceneRenderer(Options.Create(manimOptions), runner);
var fileStore = new LocalAssetFileStore(Options.Create(storage));

var narrationDuration = (await inspector.InspectAsync(narration, CancellationToken.None))
    .DurationSeconds;

var plans = SceneVisualPlanner.PlanAll(storyboard, manimRenderer.IsEnabled);

// Narration-aware fallback: proportional to scene text length, with a higher
// floor for animated scenes so each template can finish.
var weights = storyboard.Scenes
    .Select(scene => SceneTiming.WeightFor(scene.Heading, scene.Visual))
    .ToArray();
var minimums = plans
    .Select(plan => plan.Engine == SceneVisualEngine.ManimAnimation
        ? SceneTiming.AnimationMinimumSeconds
        : SceneTiming.DefaultMinimumSeconds)
    .ToArray();
var durations = SceneTiming.Allocate(weights, narrationDuration, minimums);

// Re-time the burned subtitle to the derived scene boundaries so the heading
// matches the visible scene (the canonical subtitle asset is left untouched).
var subtitleBuilder = new System.Text.StringBuilder();
var cursor = 0d;
for (var index = 0; index < storyboard.Scenes.Count; index++)
{
    subtitleBuilder.AppendLine((index + 1).ToString());
    subtitleBuilder.AppendLine(
        $"{FormatTimestamp(cursor)} --> {FormatTimestamp(cursor + durations[index])}");
    subtitleBuilder.AppendLine(storyboard.Scenes[index].Heading);
    subtitleBuilder.AppendLine();
    cursor += durations[index];
}

var demoSubtitle = Path.Combine(assetsRoot, "vq2-demo/subtitle-v2.srt");
await File.WriteAllTextAsync(demoSubtitle, subtitleBuilder.ToString());
Console.WriteLine($"subtitle={demoSubtitle} total={cursor:0.###}s");

var sceneInputs = new List<SceneMediaInput>(plans.Count);
for (var index = 0; index < plans.Count; index++)
{
    var plan = plans[index];
    var animated = plan.Engine == SceneVisualEngine.ManimAnimation;
    var extension = animated ? "mp4" : "png";
    var bytes = animated
        ? await manimRenderer.RenderAsync(
            plan.Template,
            plan.Animation! with { DurationSeconds = durations[index] },
            CancellationToken.None)
        : await svgRenderer.RenderPngAsync(plan.Brief, CancellationToken.None);

    var info = await fileStore.WriteAsync(
        $"vq2-demo/generated/scene_{index}.{extension}",
        bytes,
        CancellationToken.None);

    sceneInputs.Add(new SceneMediaInput(
        info.AbsolutePath,
        animated ? AssetType.Video : AssetType.Image,
        VisualKind: SceneVisualKind.Graphic,
        Weight: weights[index]));

    Console.WriteLine(
        $"scene {index} layout={plan.Brief.Layout} engine={plan.Engine} template={plan.Template} duration={durations[index]:0.###}s weight={weights[index]:0} bytes={info.ByteSize}");
}

Console.WriteLine($"total={durations.Sum():0.###}s narration={narrationDuration:0.###}s");

var animatedOutput = await renderer.RenderAsync(
    new VideoRenderRequest(
        sceneInputs,
        narration,
        "renders/vq2-demo/animated-explainer-v2.mp4",
        demoSubtitle),
    CancellationToken.None);

Console.WriteLine(
    $"mode={mode} path={animatedOutput.RelativePath} bytes={animatedOutput.ByteSize} hash={animatedOutput.ContentHash} duration={animatedOutput.DurationSeconds:0.###}");
return 0;

static string FormatTimestamp(double seconds)
{
    var span = TimeSpan.FromSeconds(seconds);
    return $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00},{span.Milliseconds:000}";
}
