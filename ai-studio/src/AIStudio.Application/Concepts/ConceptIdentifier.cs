using System.Diagnostics.CodeAnalysis;

namespace AIStudio.Application.Concepts;

/// <summary>
/// Validates the small declarative identifiers a concept may carry — format,
/// style and tags. These stay plain strings/data rather than enums, so a future
/// AI planner (or data loader) can introduce values such as <c>anime-short</c> or
/// <c>anime-cinematic</c> without recompiling the application. Values are data
/// only: they never name a type, provider, path, or executable.
/// </summary>
public static class ConceptIdentifier
{
    public const int MinimumLength = 1;
    public const int MaximumLength = 64;

    /// <summary>
    /// Valid: 1..64 characters, first character a lowercase letter or digit, then
    /// lowercase letters, digits, '.', '_' or '-'. Examples: <c>youtube-short</c>,
    /// <c>anime-cinematic</c>, <c>local.3d_story</c>.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            return false;
        }

        if (!IsLeadCharacter(value[0]))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!IsTrailingCharacter(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLeadCharacter(char character) =>
        (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9');

    private static bool IsTrailingCharacter(char character) =>
        (character >= 'a' && character <= 'z')
        || (character >= '0' && character <= '9')
        || character == '.'
        || character == '_'
        || character == '-';
}
