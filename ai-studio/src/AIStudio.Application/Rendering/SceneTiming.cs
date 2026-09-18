namespace AIStudio.Application.Rendering;

/// <summary>
/// Deterministic scene-timing allocation. The equal split is replaced by a
/// proportional split driven by caller-supplied weights (scene text length today)
/// with an absolute floor per scene. Animated scenes use the higher floor so a
/// template has time to finish. No speech recognition, model call, or alignment.
/// </summary>
public static class SceneTiming
{
    /// <summary>Floor for a static scene; keeps a readable beat without dead gaps.</summary>
    public const double DefaultMinimumSeconds = 2.0;

    /// <summary>Floor for an animated scene; must fit one template's full sequence.</summary>
    public const double AnimationMinimumSeconds = 3.5;

    /// <summary>Weight assigned when a scene has no usable text.</summary>
    public const double MinimumWeight = 1.0;

    /// <summary>Text-length weight fallback used when no segmentation exists.</summary>
    public static double WeightFor(string? heading, string? visual) =>
        Math.Max(MinimumWeight, (heading?.Length ?? 0) + (visual?.Length ?? 0));

    /// <summary>
    /// Splits <paramref name="totalSeconds"/> in proportion to
    /// <paramref name="weights"/>, then raises any scene below its
    /// <paramref name="minimums"/> floor and redistributes the difference. The
    /// result always sums to <paramref name="totalSeconds"/>. If the floors cannot
    /// fit, it falls back to a pure proportional split.
    /// </summary>
    public static double[] Allocate(
        IReadOnlyList<double> weights,
        double totalSeconds,
        IReadOnlyList<double> minimums)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(minimums);

        if (totalSeconds <= 0)
        {
            throw new RenderVideoException(
                "render_narration_duration_invalid",
                "Narration duration must be greater than zero.");
        }

        if (weights.Count < 1)
        {
            throw new RenderVideoException(
                "render_scene_count_invalid",
                "At least one scene is required.");
        }

        if (minimums.Count != weights.Count)
        {
            throw new ArgumentException(
                "Weights and minimums must have the same length.",
                nameof(minimums));
        }

        var count = weights.Count;
        var effectiveWeights = new double[count];
        var weightSum = 0d;
        var minimumSum = 0d;
        for (var index = 0; index < count; index++)
        {
            effectiveWeights[index] = weights[index] > 0 ? weights[index] : 1.0;
            weightSum += effectiveWeights[index];
            minimumSum += Math.Max(0, minimums[index]);
        }

        var result = new double[count];
        if (minimumSum >= totalSeconds)
        {
            // The floors cannot fit; keep a proportional split instead.
            for (var index = 0; index < count; index++)
            {
                result[index] = totalSeconds * effectiveWeights[index] / weightSum;
            }

            return result;
        }

        var fixedScenes = new bool[count];
        for (var pass = 0; pass <= count; pass++)
        {
            var fixedSum = 0d;
            var freeWeight = 0d;
            for (var index = 0; index < count; index++)
            {
                if (fixedScenes[index])
                {
                    fixedSum += result[index];
                }
                else
                {
                    freeWeight += effectiveWeights[index];
                }
            }

            if (freeWeight <= 0)
            {
                break;
            }

            var remaining = totalSeconds - fixedSum;
            var clamped = false;
            for (var index = 0; index < count; index++)
            {
                if (fixedScenes[index])
                {
                    continue;
                }

                var share = remaining * effectiveWeights[index] / freeWeight;
                if (share < minimums[index])
                {
                    share = minimums[index];
                    fixedScenes[index] = true;
                    clamped = true;
                }

                result[index] = share;
            }

            if (!clamped)
            {
                break;
            }
        }

        return result;
    }
}
