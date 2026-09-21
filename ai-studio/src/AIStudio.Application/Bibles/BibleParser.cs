using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Raised when untrusted structured bible output cannot be accepted as a valid
/// character or world bible. Malformed output fails clearly; it is never repaired
/// or executed.
/// </summary>
public sealed class BibleException : Exception
{
    public BibleException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable bible parser error codes.</summary>
public static class BibleParserErrorCodes
{
    public const string InvalidJson = "bible_invalid_json";
    public const string BibleInvalid = "bible_invalid";
}

/// <summary>
/// Parses untrusted structured output into a validated character or world bible.
/// It accepts strict JSON only and performs no speculative repair. The contract is
/// closed: unknown members are rejected, so a bible cannot smuggle executable
/// instructions (type names, commands, paths, provider workflows) through extra
/// fields. Asset references stay ids only.
/// </summary>
public static class BibleParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static CharacterBible ParseCharacter(string json) =>
        Parse<CharacterBible>(json, "character bible", static bible => bible.Validate());

    public static WorldBible ParseWorld(string json) =>
        Parse<WorldBible>(json, "world bible", static world => world.Validate());

    private static T Parse<T>(string json, string label, Func<T, IReadOnlyList<BibleIssue>> validate)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new BibleException(
                BibleParserErrorCodes.InvalidJson,
                $"The {label} response was empty.");
        }

        T? entity;

        try
        {
            entity = JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new BibleException(
                BibleParserErrorCodes.InvalidJson,
                $"The {label} response is not valid JSON for a {label}.",
                exception);
        }

        if (entity is null)
        {
            throw new BibleException(
                BibleParserErrorCodes.InvalidJson,
                $"The {label} response did not contain a {label}.");
        }

        var issues = validate(entity);
        if (issues.Count > 0)
        {
            throw new BibleException(
                BibleParserErrorCodes.BibleInvalid,
                $"The {label} is not structurally valid: {issues[0].Code}.");
        }

        return entity;
    }
}
