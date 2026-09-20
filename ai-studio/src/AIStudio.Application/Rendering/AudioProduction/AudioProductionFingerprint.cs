using System.Security.Cryptography;
using System.Text;

namespace AIStudio.Application.Rendering.AudioProduction;

/// <summary>
/// Computes the deterministic stage fingerprints used by the workspace manifest.
/// A changed fingerprint is the only reason a valid stage is regenerated; a plain
/// job retry keeps the same fingerprint.
/// </summary>
public static class AudioProductionFingerprint
{
    public static string Compute(string stage, params string[] inputs)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(inputs);

        var builder = new StringBuilder(stage)
            .Append('|')
            .Append(AudioProductionWorkspace.Version);

        foreach (var input in inputs)
        {
            builder.Append('|').Append(input ?? string.Empty);
        }

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }
}
