using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Deterministic per-scene fingerprint of the routed visual plan (engine, template,
/// direction, choreography beats and composed content). It is recorded on the
/// generated <c>SceneAsset.Creator</c> as <c>{Engine}+{fingerprint}</c>, which gives
/// narrow per-scene invalidation without a schema change: a re-run reuses a scene
/// whose plan is unchanged and regenerates only the scenes whose plan changed.
/// </summary>
public static class SceneVisualFingerprint
{
    public static string Creator(SceneVisualPlan plan) =>
        $"{plan.Engine}+{Compute(plan)[..12]}";

    public static string EngineFromCreator(string? creator) =>
        string.IsNullOrWhiteSpace(creator)
            ? string.Empty
            : creator.Split('+')[0];

    public static string Compute(SceneVisualPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder()
            .Append(SceneVisualPlanner.PlannerVersion).Append('|')
            .Append(plan.Brief.SceneIndex).Append('|')
            .Append(plan.Engine).Append('|')
            .Append(plan.Template).Append('|')
            .Append(plan.ThreeDTemplate).Append('|')
            .Append(plan.IntendedEngine).Append('|')
            .Append(plan.IsFallback).Append('|')
            .Append(Number(plan.Direction?.DurationSeconds ?? 0)).Append('|')
            .Append(plan.Brief.Heading).Append('|')
            .Append(plan.Brief.Kicker).Append('|')
            .Append(plan.Brief.Layout).Append('|')
            .Append(plan.Brief.Palette).Append('|')
            .Append(plan.Brief.Note).Append('|')
            .Append(plan.Brief.Command).Append('|')
            .Append(Number(plan.Brief.Progress)).Append('|');

        foreach (var card in plan.Brief.Cards)
        {
            builder.Append(card.Title).Append('~')
                .Append(card.Detail).Append('~')
                .Append(card.Icon).Append('|');
        }

        if (plan.Direction is not null)
        {
            builder.Append(plan.Direction.Intent).Append('|')
                .Append(plan.Direction.Composition).Append('|')
                .Append(plan.Direction.MotionStyle).Append('|');

            foreach (var beat in plan.Direction.Choreography.Beats)
            {
                builder.Append(Number(beat.StartTime)).Append('~')
                    .Append(Number(beat.Duration)).Append('~')
                    .Append(beat.Primitive).Append('~')
                    .Append(beat.Element).Append('~')
                    .Append(beat.Text).Append('~')
                    .Append(Number(beat.Value)).Append('|');
            }
        }

        if (plan.Animation is not null)
        {
            builder.Append(plan.Animation.Command).Append('|')
                .Append(string.Join(',', plan.Animation.Steps ?? [])).Append('|')
                .Append(Number(plan.Animation.ProgressTarget));
        }

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
