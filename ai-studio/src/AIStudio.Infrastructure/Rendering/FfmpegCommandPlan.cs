using System.Globalization;
using System.Text;
using AIStudio.Application.Rendering;
using AIStudio.Domain.Assets;

namespace AIStudio.Infrastructure.Rendering;

public static class FfmpegCommandPlan
{
    public static double SceneDurationSeconds(double narrationSeconds, int sceneCount)
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
        return narrationSeconds / sceneCount;
    }

    public static IReadOnlyList<string> Build(
        IReadOnlyList<SceneMediaInput> scenes,
        string narrationPath,
        string outputPath,
        double sceneDurationSeconds,
        int width,
        int height,
        int frameRate,
        string? subtitlePath = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        var duration = Format(sceneDurationSeconds);
        var arguments = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };

        foreach (var scene in scenes)
        {
            if (scene.Type == AssetType.Image)
            {
                arguments.Add("-loop");
                arguments.Add("1");
            }

            arguments.Add("-t");
            arguments.Add(duration);
            arguments.Add("-i");
            arguments.Add(scene.AbsolutePath);
        }

        arguments.Add("-i");
        arguments.Add(narrationPath);

        if (subtitlePath is not null)
        {
            arguments.Add("-i");
            arguments.Add(subtitlePath);
        }

        arguments.Add("-filter_complex");
        arguments.Add(BuildFilterGraph(scenes.Count, duration, width, height, frameRate));

        arguments.Add("-map");
        arguments.Add("[v]");
        arguments.Add("-map");
        arguments.Add($"{scenes.Count}:a");

        if (subtitlePath is not null)
        {
            arguments.Add("-map");
            arguments.Add($"{scenes.Count + 1}:s:0");
        }

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

        if (subtitlePath is not null)
        {
            arguments.Add("-c:s");
            arguments.Add("mov_text");
        }

        arguments.Add("-shortest");
        arguments.Add("-movflags");
        arguments.Add("+faststart");
        arguments.Add("-f");
        arguments.Add("mp4");
        arguments.Add(outputPath);

        return arguments;
    }

    private static string BuildFilterGraph(
        int sceneCount,
        string duration,
        int width,
        int height,
        int frameRate)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < sceneCount; index++)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"[{index}:v]tpad=stop_mode=clone:stop_duration={duration},trim=duration={duration},scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={frameRate},format=yuv420p[v{index}];");
        }

        for (var index = 0; index < sceneCount; index++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"[v{index}]");
        }

        builder.Append(CultureInfo.InvariantCulture,
            $"concat=n={sceneCount}:v=1:a=0[v]");

        return builder.ToString();
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
