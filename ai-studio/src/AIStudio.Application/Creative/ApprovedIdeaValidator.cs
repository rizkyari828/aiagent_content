using AIStudio.Application.Concepts;

namespace AIStudio.Application.Creative;

/// <summary>
/// Narrow structural validation for an approved idea. It only proves the idea is
/// usable creative input; it never judges whether the idea is good, popular, or
/// profitable, and it never re-generates or re-searches the idea.
/// </summary>
public static class ApprovedIdeaValidator
{
    public const int MaximumTextLength = 2_000;

    public static IReadOnlyList<CreativeIssue> Validate(ApprovedIdea idea)
    {
        ArgumentNullException.ThrowIfNull(idea);

        var issues = new List<CreativeIssue>();

        if (string.IsNullOrWhiteSpace(idea.Topic))
        {
            issues.Add(Issue(CreativeIssueCodes.IdeaTopicEmpty, "An approved idea topic is required."));
        }

        if (string.IsNullOrWhiteSpace(idea.Angle))
        {
            issues.Add(Issue(CreativeIssueCodes.IdeaAngleEmpty, "An approved idea angle is required."));
        }

        if (string.IsNullOrWhiteSpace(idea.Audience))
        {
            issues.Add(Issue(CreativeIssueCodes.IdeaAudienceEmpty, "An approved idea audience is required."));
        }

        if (string.IsNullOrWhiteSpace(idea.HookPremise))
        {
            issues.Add(Issue(CreativeIssueCodes.IdeaHookEmpty, "An approved idea hook premise is required."));
        }

        var constraints = idea.Constraints ?? new CreativeConstraints();

        if (!string.IsNullOrWhiteSpace(constraints.PreferredFormat)
            && !ConceptIdentifier.IsValid(constraints.PreferredFormat))
        {
            issues.Add(Issue(
                CreativeIssueCodes.IdeaPreferredFormatInvalid,
                "A preferred format must be a lowercase identifier such as 'youtube-short'."));
        }

        if (!string.IsNullOrWhiteSpace(constraints.PreferredStyle)
            && !ConceptIdentifier.IsValid(constraints.PreferredStyle))
        {
            issues.Add(Issue(
                CreativeIssueCodes.IdeaPreferredStyleInvalid,
                "A preferred style must be a lowercase identifier such as 'clean-tech'."));
        }

        if (constraints.TargetDurationSeconds is { } duration
            && (duration < ConceptValidator.MinimumDurationSeconds
                || duration > ConceptValidator.MaximumDurationSeconds))
        {
            issues.Add(Issue(
                CreativeIssueCodes.IdeaDurationInvalid,
                $"A target duration must be between {ConceptValidator.MinimumDurationSeconds} and {ConceptValidator.MaximumDurationSeconds} seconds."));
        }

        return issues;
    }

    private static CreativeIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
