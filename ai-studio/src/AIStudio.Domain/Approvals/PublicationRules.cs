namespace AIStudio.Domain.Approvals;

/// <summary>
/// Shared, closed validation for the platform token, video URL, and video id that
/// both a final approval (destination) and a publication record carry. Values are
/// plain data: no provider, credential, or upload concern is modeled here.
/// </summary>
public static class PublicationRules
{
    public const int MinPlatformLength = 2;
    public const int MaxPlatformLength = 32;
    public const int MinVideoIdLength = 3;
    public const int MaxVideoIdLength = 64;
    public const int MaxUrlLength = 2048;
    public const int MaxNotesLength = 2000;
    public const int ContentHashLength = 64;

    /// <summary>
    /// YouTube is the only platform with a host rule in v1; other platforms are
    /// accepted as lowercase tokens and still require an https URL.
    /// </summary>
    public const string YouTube = "youtube";

    private static readonly HashSet<string> YouTubeHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "youtu.be",
        "www.youtu.be"
    };

    public static string NormalizePlatform(string? value, string parameterName = "platform")
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)
            || normalized.Length < MinPlatformLength
            || normalized.Length > MaxPlatformLength
            || !char.IsAsciiLetterLower(normalized[0])
            || !normalized.All(character =>
                char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '-'))
        {
            throw new ArgumentException(
                $"Platform must be a lowercase token of {MinPlatformLength} to {MaxPlatformLength} characters.",
                parameterName);
        }

        return normalized;
    }

    /// <summary>
    /// A video id is an opaque platform identifier, never a URL or path. The closed
    /// character set already excludes <c>:</c>, <c>/</c>, and whitespace.
    /// </summary>
    public static string NormalizeVideoId(string? value, string parameterName = "videoId")
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)
            || normalized.Length < MinVideoIdLength
            || normalized.Length > MaxVideoIdLength
            || !normalized.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character == '_'
                || character == '-'))
        {
            throw new ArgumentException(
                $"Video id must be {MinVideoIdLength} to {MaxVideoIdLength} letters, digits, underscores, or dashes.",
                parameterName);
        }

        return normalized;
    }

    public static string NormalizeVideoUrl(
        string? value,
        string platform,
        string parameterName = "url")
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxUrlLength)
        {
            throw new ArgumentException(
                $"Video URL must contain 1 to {MaxUrlLength} characters.",
                parameterName);
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException(
                "Video URL must be an absolute https URL without credentials.",
                parameterName);
        }

        if (string.Equals(platform, YouTube, StringComparison.Ordinal)
            && !YouTubeHosts.Contains(uri.Host))
        {
            throw new ArgumentException(
                "A YouTube publication URL must use a youtube.com or youtu.be host.",
                parameterName);
        }

        return normalized;
    }

    public static string? OptionalNotes(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length > MaxNotesLength
            ? throw new ArgumentException(
                $"Notes must contain at most {MaxNotesLength} characters.",
                parameterName)
            : normalized;
    }

    public static string RequireHash(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != ContentHashLength
            || !value.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "Content hash must be a 64-character hexadecimal SHA-256 value.",
                parameterName);
        }

        return value.ToLowerInvariant();
    }
}
