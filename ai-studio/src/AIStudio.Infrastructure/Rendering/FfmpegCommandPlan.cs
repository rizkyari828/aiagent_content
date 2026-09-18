using System.Globalization;
using System.Text;
using AIStudio.Application.Rendering;
using AIStudio.Domain.Assets;

namespace AIStudio.Infrastructure.Rendering;

public static class FfmpegCommandPlan
{
    // Content-safety policy: without explicit direction the renderer only uses
    // centered zoom, which never pushes frame content out of view. Pan is opt-in
    // via SceneMediaInput.Motion and reserved for visuals that declare safe
    // margins; do not infer this with computer vision.
    private static readonly SceneMotion[] DefaultMotionCycle =
    [
        SceneMotion.SlowZoomIn,
        SceneMotion.SlowZoomOut
    ];

    private const string DuckThreshold = "0.05";
    private const string DuckRatio = "4";
    private const string DuckAttack = "20";
    private const string DuckRelease = "400";
    // Narration is gain-staged to a standard target before mixing so a quiet
    // recording stays audible and reliably triggers ducking. Applied only when
    // the render actually mixes audio; a narration-only render is untouched.
    private const string NarrationLoudness = "loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000,asetpts=PTS-STARTPTS";
    private const string MusicHighPass = "120";

    public static double SceneDurationSeconds(
        double narrationSeconds,
        int sceneCount,
        SceneTransition transition = SceneTransition.Cut,
        double transitionDurationSeconds = 0.35)
    {
        if (narrationSeconds <= 0)
        {
            throw new RenderVideoException(
                "render_narration_duration_invalid",
                "Narration duration must be greater than zero.");
        }

        if (sceneCount < 1)
        {
            throw new RenderVideoException(
                "render_scene_count_invalid",
                "At least one scene is required.");
        }

        // ponytail: equal scene duration = narration length / scene count; add per-scene narration timing when available.
        if (transition == SceneTransition.Crossfade && sceneCount > 1)
        {
            return (narrationSeconds + (sceneCount - 1) * transitionDurationSeconds) / sceneCount;
        }

        return narrationSeconds / sceneCount;
    }

    public static IReadOnlyList<string> Build(
        IReadOnlyList<SceneMediaInput> scenes,
        string narrationPath,
        string outputPath,
        VideoRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(settings);

        if (scenes.Count == 0)
        {
            throw new RenderVideoException(
                "render_scene_count_invalid",
                "At least one scene is required.");
        }

        var sceneCount = scenes.Count;
        var transitionDuration = settings.TransitionDurationSeconds;
        var sceneDurations = ResolveSceneDurations(scenes, settings, transitionDuration);

        if (settings.Transition != SceneTransition.Cut
            && (transitionDuration <= 0 || transitionDuration >= sceneDurations.Min()))
        {
            throw new RenderVideoException(
                "render_transition_invalid",
                "Transition duration must be greater than zero and shorter than a scene.");
        }

        var arguments = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
        var motions = new SceneMotion[sceneCount];

        for (var index = 0; index < sceneCount; index++)
        {
            var scene = scenes[index];
            motions[index] = ResolveMotion(scene, index, settings.EnableMotion);

            if (scene.Type == AssetType.Image && motions[index] != SceneMotion.None)
            {
                // zoompan generates the frames; no -loop/-t needed.
                arguments.Add("-i");
                arguments.Add(scene.AbsolutePath);
                continue;
            }

            if (scene.Type == AssetType.Image)
            {
                arguments.Add("-loop");
                arguments.Add("1");
            }

            arguments.Add("-t");
            arguments.Add(Format(sceneDurations[index]));
            arguments.Add("-i");
            arguments.Add(scene.AbsolutePath);
        }

        arguments.Add("-i");
        arguments.Add(narrationPath);
        var narrationIndex = sceneCount;
        var inputIndex = sceneCount + 1;

        int? musicIndex = null;
        if (settings.BackgroundMusic is not null)
        {
            arguments.Add("-stream_loop");
            arguments.Add("-1");
            arguments.Add("-i");
            arguments.Add(settings.BackgroundMusic.AbsolutePath);
            musicIndex = inputIndex;
            inputIndex++;
        }

        var soundEffectIndexes = new Dictionary<int, int>();
        for (var index = 0; index < sceneCount; index++)
        {
            if (scenes[index].SoundEffect is null)
            {
                continue;
            }

            arguments.Add("-i");
            arguments.Add(scenes[index].SoundEffect!.AbsolutePath);
            soundEffectIndexes[index] = inputIndex;
            inputIndex++;
        }

        arguments.Add("-filter_complex");
        arguments.Add(BuildFilterGraph(
            scenes,
            motions,
            settings,
            sceneDurations,
            narrationIndex,
            musicIndex,
            soundEffectIndexes));

        arguments.Add("-map");
        arguments.Add("[v]");
        arguments.Add("-map");
        arguments.Add(settings.BackgroundMusic is not null || soundEffectIndexes.Count > 0
            ? "[a]"
            : $"{narrationIndex}:a");

        arguments.Add("-c:v");
        arguments.Add("libx264");
        arguments.Add("-preset");
        arguments.Add("veryfast");
        arguments.Add("-crf");
        arguments.Add("23");
        arguments.Add("-pix_fmt");
        arguments.Add("yuv420p");
        arguments.Add("-c:a");
        arguments.Add("aac");
        arguments.Add("-b:a");
        arguments.Add("128k");

        arguments.Add("-shortest");
        arguments.Add("-movflags");
        arguments.Add("+faststart");
        arguments.Add("-f");
        arguments.Add("mp4");
        arguments.Add(outputPath);

        return arguments;
    }

    private static string BuildFilterGraph(
        IReadOnlyList<SceneMediaInput> scenes,
        IReadOnlyList<SceneMotion> motions,
        VideoRenderSettings settings,
        IReadOnlyList<double> sceneDurations,
        int narrationIndex,
        int? musicIndex,
        IReadOnlyDictionary<int, int> soundEffectIndexes)
    {
        var builder = new StringBuilder();
        var frameRate = settings.FrameRate;

        for (var index = 0; index < scenes.Count; index++)
        {
            var duration = Format(sceneDurations[index]);
            builder.Append(CultureInfo.InvariantCulture, $"[{index}:v]");

            if (scenes[index].Type == AssetType.Image && motions[index] != SceneMotion.None)
            {
                var frames = Math.Max(2, (int)Math.Ceiling(sceneDurations[index] * frameRate));
                builder.Append(CultureInfo.InvariantCulture,
                    $"scale={settings.Width}:{settings.Height}:force_original_aspect_ratio=increase,");
                builder.Append(CultureInfo.InvariantCulture,
                    $"crop={settings.Width}:{settings.Height},");
                builder.Append(BuildMotion(motions[index], frames, settings));
                builder.Append(",setsar=1,format=yuv420p");
            }
            else
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"tpad=stop_mode=clone:stop_duration={duration},trim=duration={duration},");
                builder.Append(CultureInfo.InvariantCulture,
                    $"scale={settings.Width}:{settings.Height}:force_original_aspect_ratio=increase,");
                builder.Append(CultureInfo.InvariantCulture,
                    $"crop={settings.Width}:{settings.Height},setsar=1,fps={frameRate},format=yuv420p");
            }

            if (settings.Transition == SceneTransition.Fade)
            {
                var transition = Format(settings.TransitionDurationSeconds);
                var fadeOutStart = Format(sceneDurations[index] - settings.TransitionDurationSeconds);
                builder.Append(CultureInfo.InvariantCulture,
                    $",fade=t=in:st=0:d={transition},fade=t=out:st={fadeOutStart}:d={transition}");
            }

            builder.Append(CultureInfo.InvariantCulture, $"[v{index}];");
        }

        var hasSubtitleStyle = settings.SubtitlePath is not null;
        var assembledLabel = hasSubtitleStyle ? "vcat" : "v";

        if (settings.Transition == SceneTransition.Crossfade && scenes.Count > 1)
        {
            var previous = "v0";
            var previousKind = ResolveVisualKind(scenes[0]);
            var cumulative = sceneDurations[0];
            for (var index = 1; index < scenes.Count; index++)
            {
                var kind = ResolveVisualKind(scenes[index]);
                var transition = CrossfadeTransition(previousKind, kind);
                var offset = Format(cumulative - index * settings.TransitionDurationSeconds);
                var output = index == scenes.Count - 1 ? assembledLabel : $"x{index}";
                builder.Append(CultureInfo.InvariantCulture,
                    $"[{previous}][v{index}]xfade=transition={transition}:duration={Format(settings.TransitionDurationSeconds)}:offset={offset}[{output}];");
                previous = output;
                previousKind = kind;
                cumulative += sceneDurations[index];
            }
        }
        else
        {
            for (var index = 0; index < scenes.Count; index++)
            {
                builder.Append(CultureInfo.InvariantCulture, $"[v{index}]");
            }

            builder.Append(CultureInfo.InvariantCulture,
                $"concat=n={scenes.Count}:v=1:a=0[{assembledLabel}];");
        }

        if (hasSubtitleStyle)
        {
            var style = settings.Subtitle ?? new SubtitleStyle();
            builder.Append(CultureInfo.InvariantCulture,
                $"[vcat]subtitles=filename='{EscapeFilterValue(settings.SubtitlePath!)}':force_style='{BuildForceStyle(style)}'[v];");
        }

        AppendAudioGraph(
            builder,
            scenes,
            settings,
            narrationIndex,
            musicIndex,
            soundEffectIndexes);

        return builder.ToString();
    }

    private static void AppendAudioGraph(
        StringBuilder builder,
        IReadOnlyList<SceneMediaInput> scenes,
        VideoRenderSettings settings,
        int narrationIndex,
        int? musicIndex,
        IReadOnlyDictionary<int, int> soundEffectIndexes)
    {
        var music = settings.BackgroundMusic;
        var needsMix = music is not null || soundEffectIndexes.Count > 0;
        var mixes = new List<string>();

        if (needsMix)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"[{narrationIndex}:a]{NarrationLoudness}[nargain];");
        }

        var narrationLabel = needsMix ? "nargain" : $"{narrationIndex}:a";

        if (music is not null && music.Duck)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"[{narrationLabel}]asplit=2[nar][duckkey];");
            mixes.Add("nar");
        }
        else
        {
            mixes.Add(narrationLabel);
        }

        if (music is not null && musicIndex is not null)
        {
            var fadeSeconds = Math.Min(1d, settings.NarrationDurationSeconds / 2);
            var fade = Format(fadeSeconds);
            var fadeOutStart = Format(Math.Max(0, settings.NarrationDurationSeconds - fadeSeconds));
            builder.Append(CultureInfo.InvariantCulture,
                $"[{musicIndex}:a]highpass=f={MusicHighPass},volume={Format(music.Volume)},afade=t=in:st=0:d={fade},afade=t=out:st={fadeOutStart}:d={fade}[music];");

            if (music.Duck)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"[music][duckkey]sidechaincompress=threshold={DuckThreshold}:ratio={DuckRatio}:attack={DuckAttack}:release={DuckRelease}[musicduck];");
                mixes.Add("musicduck");
            }
            else
            {
                mixes.Add("music");
            }
        }

        var effectIndex = 0;
        foreach (var sceneIndex in soundEffectIndexes.Keys.OrderBy(index => index))
        {
            var effect = scenes[sceneIndex].SoundEffect!;
            var delayMilliseconds = (int)Math.Round(Math.Max(0, effect.StartOffsetSeconds) * 1000);
            var label = $"sfx{effectIndex}";
            builder.Append(CultureInfo.InvariantCulture,
                $"[{soundEffectIndexes[sceneIndex]}:a]adelay={delayMilliseconds}:all=1,volume={Format(effect.Volume)}[{label}];");
            mixes.Add(label);
            effectIndex++;
        }

        if (mixes.Count == 1)
        {
            return;
        }

        builder.Append(CultureInfo.InvariantCulture,
            $"[{string.Join("][", mixes)}]amix=inputs={mixes.Count}:duration=first:normalize=0[a];");
    }

    // Transition ghosting policy: a crossfade briefly overlays the outgoing and
    // incoming frames. When either side is text-heavy / UI / infographic the
    // overlay reads as double text, so that boundary fades through the background
    // instead (xfade fadeblack). Photographic boundaries keep the crossfade. The
    // overlap duration is unchanged, so scene timing and offsets are unaffected.
    // Classification is explicit and deterministic; no computer vision is used.
    private static SceneVisualKind ResolveVisualKind(SceneMediaInput scene) =>
        scene.VisualKind ?? SceneVisualKind.Photographic;

    private static string CrossfadeTransition(SceneVisualKind left, SceneVisualKind right) =>
        left == SceneVisualKind.Graphic || right == SceneVisualKind.Graphic
            ? "fadeblack"
            : "fade";

    /// <summary>
    /// Allocates the narration across scenes by weight (scene text length today),
    /// using a higher floor for animated scenes. The content split sums to the
    /// narration; the crossfade overlap budget is then distributed proportionally
    /// so the final timeline still equals the narration duration.
    /// </summary>
    private static double[] ResolveSceneDurations(
        IReadOnlyList<SceneMediaInput> scenes,
        VideoRenderSettings settings,
        double transitionDuration)
    {
        var weights = new double[scenes.Count];
        var minimums = new double[scenes.Count];
        for (var index = 0; index < scenes.Count; index++)
        {
            weights[index] = scenes[index].Weight > 0 ? scenes[index].Weight : 1.0;
            minimums[index] = scenes[index].Type == AssetType.Video
                ? SceneTiming.AnimationMinimumSeconds
                : SceneTiming.DefaultMinimumSeconds;
        }

        var contentDurations = SceneTiming.Allocate(
            weights,
            settings.NarrationDurationSeconds,
            minimums);

        var overlapBudget = settings.Transition == SceneTransition.Crossfade && scenes.Count > 1
            ? (scenes.Count - 1) * transitionDuration
            : 0;

        var contentSum = 0d;
        foreach (var value in contentDurations)
        {
            contentSum += value;
        }

        var scale = (settings.NarrationDurationSeconds + overlapBudget) / contentSum;
        var durations = new double[scenes.Count];
        for (var index = 0; index < scenes.Count; index++)
        {
            durations[index] = contentDurations[index] * scale;
        }

        return durations;
    }

    private static SceneMotion ResolveMotion(
        SceneMediaInput scene,
        int index,
        bool enableMotion)
    {
        if (!enableMotion || scene.Type != AssetType.Image)
        {
            return SceneMotion.None;
        }

        return scene.Motion ?? DefaultMotionCycle[index % DefaultMotionCycle.Length];
    }

    private static string BuildMotion(
        SceneMotion motion,
        int frames,
        VideoRenderSettings settings)
    {
        var last = frames - 1;
        var centerX = "iw/2-(iw/zoom/2)";
        var centerY = "ih/2-(ih/zoom/2)";

        var (zoom, x, y) = motion switch
        {
            SceneMotion.SlowZoomIn => (
                $"1+0.08*on/{last}",
                centerX,
                centerY),
            SceneMotion.SlowZoomOut => (
                $"1.08-0.08*on/{last}",
                centerX,
                centerY),
            SceneMotion.PanRight => (
                "1.08",
                $"(iw-iw/zoom)*on/{last}",
                centerY),
            SceneMotion.PanLeft => (
                "1.08",
                $"(iw-iw/zoom)*(1-on/{last})",
                centerY),
            _ => ("1", centerX, centerY)
        };

        return $"zoompan=z='{zoom}':x='{x}':y='{y}':d={frames}:s={settings.Width}x{settings.Height}:fps={settings.FrameRate}";
    }

    private static string BuildForceStyle(SubtitleStyle style)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(style.FontName))
        {
            builder.Append("FontName=").Append(style.FontName).Append(',');
        }

        builder.Append("FontSize=").Append(style.FontSize)
            .Append(",PrimaryColour=&H00FFFFFF")
            .Append(",OutlineColour=&H00000000")
            .Append(",BorderStyle=1")
            .Append(",Outline=").Append(style.Outline)
            .Append(",Shadow=").Append(style.Shadow)
            .Append(",MarginV=").Append(style.MarginVertical)
            .Append(",Alignment=2");

        return builder.ToString();
    }

    /// <summary>
    /// Escapes a path for use inside a libass filtergraph value. The path is
    /// single-quoted by the caller, so only backslashes, quotes, colons and
    /// commas need protection.
    /// </summary>
    private static string EscapeFilterValue(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal);

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
