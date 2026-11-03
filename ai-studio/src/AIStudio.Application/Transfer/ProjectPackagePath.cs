namespace AIStudio.Application.Transfer;

/// <summary>
/// One closed path rule for every path that crosses the package boundary. Package paths
/// and asset-root paths share the same shape: a relative, forward-slash path with no
/// empty segment, no current/parent segment, no drive or URL separator, and no control
/// characters. Nothing is silently repaired; an unsafe path is rejected.
/// </summary>
public static class ProjectPackagePath
{
    public const int MaxPathLength = 1024;

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaxPathLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains(':', StringComparison.Ordinal)
            || value.Contains('\0', StringComparison.Ordinal)
            || value.StartsWith('/'))
        {
            return false;
        }

        var segments = value.Split('/');
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0
                || segment is "." or ".."
                || segment.Any(char.IsControl))
            {
                return false;
            }
        }

        normalized = value;
        return true;
    }
}
