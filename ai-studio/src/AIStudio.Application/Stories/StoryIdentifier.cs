using System.Diagnostics.CodeAnalysis;

namespace AIStudio.Application.Stories;

/// <summary>
/// Validates the small declarative identifiers story data may carry: story plan
/// ids, narrative pattern ids, beat ids, beat roles, importance values, and
/// character/world references. They stay plain strings/data rather than enums, so
/// a new narrative pattern (for example <c>anime-horror-reveal</c>) or a new beat
/// role (for example <c>emotional-turn</c>) needs no new C# type or enum value.
/// Values are narrative intent only: they never name a type, provider, path,
/// command, or executable.
/// </summary>
public static class StoryIdentifier
{
    public const int MinimumLength = 1;
    public const int MaximumLength = 96;

    /// <summary>
    /// Valid: 1..96 characters, first character a lowercase letter or digit, then
    /// lowercase letters, digits, '.', '_' or '-'. Examples: <c>problem-solution-short</c>,
    /// <c>anime-horror-reveal</c>, <c>tutorial-step</c>.
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
