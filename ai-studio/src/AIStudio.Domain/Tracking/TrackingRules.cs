namespace AIStudio.Domain.Tracking;

/// <summary>
/// Closed validation shared by recorded metric snapshots, time entries, and cost
/// entries. Everything here is manually recorded operator data: nothing contacts a
/// provider, and nothing converts an unknown value into zero.
/// </summary>
public static class TrackingRules
{
    public const int MinTokenLength = 2;
    public const int MaxTokenLength = 32;
    public const int MaxNotesLength = 2000;
    public const int CurrencyLength = 3;

    /// <summary>
    /// A click-through rate is a ratio in <c>0..1</c>, never a percentage. Zero is a
    /// real measured value; an unknown CTR stays <c>null</c>.
    /// </summary>
    public const decimal MaxClickThroughRate = 1m;

    /// <summary>One year of minutes; a larger single entry is a data-entry mistake.</summary>
    public const decimal MaxMinutesPerEntry = 525_600m;

    /// <summary>Fits the <c>numeric(18,4)</c> column without silent rounding overflow.</summary>
    public const decimal MaxAmount = 1_000_000_000_000m;

    /// <summary>
    /// Lowercase token used for a capture source or a free category (for example
    /// <c>manual</c>, <c>content</c>, <c>development</c>).
    /// </summary>
    public static string NormalizeToken(string? value, string parameterName)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)
            || normalized.Length < MinTokenLength
            || normalized.Length > MaxTokenLength
            || !char.IsAsciiLetterLower(normalized[0])
            || !normalized.All(character =>
                char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '-'
                || character == '_'))
        {
            throw new ArgumentException(
                $"'{parameterName}' must be a lowercase token of {MinTokenLength} to {MaxTokenLength} letters, digits, dashes, or underscores.",
                parameterName);
        }

        return normalized;
    }

    /// <summary>
    /// ISO 4217-shaped currency code: exactly three letters, stored uppercase. Input is
    /// case-insensitive; membership of the real ISO 4217 list is not verified offline.
    /// </summary>
    public static string NormalizeCurrency(string? value, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Length != CurrencyLength
            || !trimmed.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                "Currency must be an ISO 4217 three-letter code such as 'IDR' or 'USD'.",
                parameterName);
        }

        return trimmed.ToUpperInvariant();
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

    /// <summary>
    /// Null means "not measured"; zero means "measured as zero". A recorded negative
    /// counter is rejected instead of being clamped.
    /// </summary>
    public static int? OptionalCount(int? value, string parameterName)
    {
        if (value is < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"'{parameterName}' must be zero or greater.");
        }

        return value;
    }

    public static decimal? OptionalNonNegativeDecimal(decimal? value, string parameterName)
    {
        if (value is < 0m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"'{parameterName}' must be zero or greater.");
        }

        return value;
    }

    public static decimal? OptionalClickThroughRate(decimal? value, string parameterName)
    {
        if (value is < 0m or > MaxClickThroughRate)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"'{parameterName}' must be a ratio between 0 and 1.");
        }

        return value;
    }

    public static decimal RequirePositiveMinutes(decimal value, string parameterName)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"'{parameterName}' must be greater than zero.");
        }

        return value > MaxMinutesPerEntry
            ? throw new ArgumentOutOfRangeException(
                parameterName,
                $"'{parameterName}' must not exceed {MaxMinutesPerEntry} minutes.")
            : value;
    }

    public static decimal RequirePositiveAmount(decimal value, string parameterName)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"'{parameterName}' must be greater than zero.");
        }

        return value > MaxAmount
            ? throw new ArgumentOutOfRangeException(
                parameterName,
                $"'{parameterName}' must not exceed {MaxAmount}.")
            : value;
    }

    /// <summary>
    /// Recorded instants are stored as UTC. A non-default <see cref="DateTimeOffset"/>
    /// keeps its instant and is normalized to offset zero.
    /// </summary>
    public static DateTimeOffset RequireTimestamp(
        DateTimeOffset value,
        string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentException(
                $"A non-default '{parameterName}' timestamp is required.",
                parameterName);
        }

        return value.ToUniversalTime();
    }

    public static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }
}
