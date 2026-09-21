using System.Text.RegularExpressions;
using AIStudio.Application.Bibles;

namespace AIStudio.BibleSemanticValidation;

/// <summary>
/// A PASS/REVIEW/FAIL-quality signal plus short evidence. Semantic bible quality is a
/// human-review concern: these signals never fail structural validation, never mutate a
/// bible, and never repair or merge anything.
/// </summary>
public sealed record BibleSignal
{
    public string Status { get; init; } = "unavailable";

    public IReadOnlyList<string> Details { get; init; } = [];
}

/// <summary>
/// Small, deterministic, shared bible semantic-quality signals used by both the
/// story-bible and grounded-content validation harnesses. They are deliberately
/// conservative lexical signals: when evidence is weak they stay silent rather than
/// guess. No environment ontology, no semantic model, no synonym taxonomy.
/// </summary>
public static class BibleSemanticSignals
{
    private static readonly HashSet<string> AgencyRoles = new(StringComparer.Ordinal)
    {
        "protagonist", "antagonist", "narrator", "companion", "guide", "mentor", "guardian", "hero", "villain"
    };

    private static readonly string[] AgencyPhrases =
    [
        "sentient", "autonomous", "personified", "anthropomorphic", "self-aware",
        "speaks", "speaking", "talks", "decides", "intends", "pursues", "agent"
    ];

    /// <summary>
    /// Flags a likely internal contradiction when a world's environment type and its
    /// identity label (id/displayName) name two different compound environment nouns
    /// sharing the same specific head (real evidence: id "playroom-01"/displayName
    /// "The Playroom" classified as environmentType "bedroom"). It never infers or
    /// rewrites a corrected environment type.
    /// ponytail: lexical shared-head heuristic by design; its ceiling is compound heads
    /// only — upgrade only if real evidence shows it is too blunt.
    /// </summary>
    public static BibleSignal WorldIdentityCoherence(IReadOnlyList<WorldBible> worlds)
    {
        if (worlds.Count == 0)
        {
            return new BibleSignal { Status = "unavailable" };
        }

        var details = new List<string>();

        foreach (var world in worlds)
        {
            var identity = world.Identity ?? new WorldIdentity();
            var environmentTokens = Tokens(identity.EnvironmentType).Distinct(StringComparer.Ordinal).ToList();
            var labelTokens = Tokens(world.Id.Value)
                .Concat(Tokens(world.DisplayName))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var environment in environmentTokens)
            {
                foreach (var label in labelTokens)
                {
                    if (!string.Equals(environment, label, StringComparison.Ordinal)
                        && SharesSpecificEnvironmentHead(environment, label))
                    {
                        details.Add($"{world.Id.Value}: environmentType '{identity.EnvironmentType}' vs identity '{label}'");
                    }
                }
            }
        }

        details = details.Distinct(StringComparer.Ordinal).ToList();

        return details.Count == 0
            ? new BibleSignal { Status = "PASS" }
            : new BibleSignal { Status = "REVIEW", Details = details };
    }

    /// <summary>
    /// Flags a likely over-extraction when an entity is proposed BOTH as a character
    /// bible and as the same world's recurring prop, without clear evidence of agency or
    /// personification. Agency evidence (an agent role or explicit personification
    /// wording) suppresses the signal, so a legitimate autonomous non-human character
    /// is never rejected merely for being associated with its environment. It never
    /// merges, removes, or edits anything.
    /// </summary>
    public static BibleSignal EntityRoleCoherence(
        IReadOnlyList<CharacterBible> characters,
        IReadOnlyList<WorldBible> worlds)
    {
        if (characters.Count == 0 || worlds.Count == 0)
        {
            return new BibleSignal { Status = "unavailable" };
        }

        var propTokens = worlds
            .SelectMany(world => (world.RecurringProps ?? []).SelectMany(Tokens))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (propTokens.Count == 0)
        {
            return new BibleSignal { Status = "PASS" };
        }

        var details = new List<string>();

        foreach (var character in characters)
        {
            if (HasAgency(character))
            {
                continue;
            }

            var entityTokens = Tokens(character.Id.Value)
                .Concat(Tokens(character.DisplayName))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var matches = entityTokens
                .Where(token => token.Length >= 4 && propTokens.Contains(token, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (matches.Count > 0)
            {
                details.Add(
                    $"{character.Id.Value}: proposed as a character but also a recurring world prop ('{string.Join(", ", matches)}')");
            }
        }

        return details.Count == 0
            ? new BibleSignal { Status = "PASS" }
            : new BibleSignal { Status = "REVIEW", Details = details };
    }

    private static bool HasAgency(CharacterBible character)
    {
        var identity = character.Identity ?? new CharacterIdentity();

        if (identity.Role is { } role && AgencyRoles.Contains(role))
        {
            return true;
        }

        var wording = string.Join(
            ' ',
            identity.VisualDescription,
            string.Join(' ', character.PersonalityTraits ?? []),
            string.Join(' ', identity.DistinguishingTraits ?? []));

        return AgencyPhrases.Any(phrase =>
            Regex.IsMatch(wording, $@"\b{Regex.Escape(phrase)}\b", RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// True when both tokens are compound nouns with a non-empty modifier sharing the
    /// same trailing head of at least four letters (for example "playroom"/"bedroom").
    /// A generic token with an empty modifier such as "room" is compatible with any
    /// "*-room" label, so it is never treated as a conflict.
    /// </summary>
    private static bool SharesSpecificEnvironmentHead(string first, string second)
    {
        var head = CommonSuffix(first, second);
        return head.Length >= 4
            && first.Length > head.Length
            && second.Length > head.Length;
    }

    private static string CommonSuffix(string first, string second)
    {
        var length = 0;
        while (length < first.Length
            && length < second.Length
            && first[^(length + 1)] == second[^(length + 1)])
        {
            length++;
        }

        return first[^length..];
    }

    private static IEnumerable<string> Tokens(string? value) =>
        Regex.Split(value ?? string.Empty, "[^A-Za-z]+")
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0);
}
