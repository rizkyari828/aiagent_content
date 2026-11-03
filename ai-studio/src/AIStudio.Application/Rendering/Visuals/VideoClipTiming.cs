using System.Globalization;

namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Deterministic fit of one source I2V clip (typically 3–5 s) onto a scene's content
/// duration. The clip is never frozen: a clip shorter than the scene is repeated whole
/// until it covers the timeline, a longer clip is trimmed at the tail, and an exact
/// match plays once with no trim.
///
/// Assumptions:
/// <list type="bullet">
/// <item>
/// The optional crossfade overlap is ADDITIVE. A scene that crossfades into the next
/// one must supply <c>content duration + overlap</c> seconds of material, because the
/// tail blend consumes extra frames beyond the scene's content. Callers pass 0 when no
/// crossfade is used.
/// </item>
/// <item>
/// The target timeline, the source clip, and the overlap are rounded half away from
/// zero to <see cref="PrecisionDecimals"/> decimals before any decision, so the result
/// is stable across runs and platforms and never differs by floating-point noise.
/// </item>
/// <item>
/// Rounding never changes a decision: it only bounds the reported numbers. The signed
/// rounding shift of the requested timeline is reported separately as
/// <see cref="RoundingResidualSeconds"/> at <see cref="ResidualPrecisionDecimals"/>
/// decimals, so a sub-millisecond shift stays visible.
/// </item>
/// </list>
/// </summary>
public sealed record VideoClipTiming(
    double SourceClipSeconds,
    double SceneContentSeconds,
    double CrossfadeOverlapSeconds,
    double TargetSeconds,
    int PlayCount,
    int RepeatCount,
    double TrimmedTailSeconds,
    double PlayedSeconds,
    double CoveredSeconds,
    double RoundingResidualSeconds)
{
    public const int PrecisionDecimals = 3;
    public const int ResidualPrecisionDecimals = 6;

    /// <summary>
    /// Fits <paramref name="sourceClipSeconds"/> onto
    /// <paramref name="sceneContentSeconds"/> (+ optional crossfade overlap).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The source clip or the scene content is zero/negative/non-finite, or the overlap
    /// is negative/non-finite.
    /// </exception>
    public static VideoClipTiming Fit(
        double sourceClipSeconds,
        double sceneContentSeconds,
        double crossfadeOverlapSeconds = 0)
    {
        var source = Normalize(sourceClipSeconds, nameof(sourceClipSeconds));
        var scene = Normalize(sceneContentSeconds, nameof(sceneContentSeconds));
        var overlap = Normalize(crossfadeOverlapSeconds, nameof(crossfadeOverlapSeconds));

        if (source <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceClipSeconds),
                sourceClipSeconds,
                "The source clip duration must be greater than zero.");
        }

        if (scene <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sceneContentSeconds),
                sceneContentSeconds,
                "The scene content duration must be greater than zero.");
        }

        if (overlap < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(crossfadeOverlapSeconds),
                crossfadeOverlapSeconds,
                "The crossfade overlap cannot be negative.");
        }

        var target = Round(scene + overlap);
        var playCount = (int)decimal.Ceiling((decimal)target / (decimal)source);
        var played = Round(playCount * source);
        var trimmedTail = Round(played - target);
        if (trimmedTail < 0)
        {
            // Defensive only: the ceiling guarantee means played >= target.
            trimmedTail = 0;
        }

        var covered = Round(played - trimmedTail);

        return new VideoClipTiming(
            source,
            scene,
            overlap,
            target,
            playCount,
            RepeatCount: playCount - 1,
            trimmedTail,
            played,
            covered,
            RoundingResidualSeconds: Math.Round(
                covered - (sceneContentSeconds + crossfadeOverlapSeconds),
                ResidualPrecisionDecimals,
                MidpointRounding.AwayFromZero));
    }

    private static double Normalize(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The duration must be a finite number.");
        }

        return Round(value);
    }

    private static double Round(double value) =>
        Math.Round(value, PrecisionDecimals, MidpointRounding.AwayFromZero);

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{nameof(VideoClipTiming)} {{ source={SourceClipSeconds:F3}, scene={SceneContentSeconds:F3}, overlap={CrossfadeOverlapSeconds:F3}, target={TargetSeconds:F3}, plays={PlayCount}, repeats={RepeatCount}, trim={TrimmedTailSeconds:F3}, played={PlayedSeconds:F3}, covered={CoveredSeconds:F3}, residual={RoundingResidualSeconds:F3} }}");
}
