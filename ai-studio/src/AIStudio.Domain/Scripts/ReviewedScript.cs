using System.Text.Json;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;

namespace AIStudio.Domain.Scripts;

public sealed class ReviewedScript
{
    public const int MaxContentLength = 200_000;

    private ReviewedScript()
    {
    }

    private ReviewedScript(
        Guid id,
        Guid contentProjectId,
        Guid sourceJobId,
        string content,
        DateTimeOffset createdAt)
    {
        Id = id;
        ContentProjectId = contentProjectId;
        SourceJobId = sourceJobId;
        Content = content;
        Status = ScriptReviewStatus.Draft;
        Revision = 1;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ContentProjectId { get; private set; }

    public ContentProject ContentProject { get; private set; } = null!;

    public Guid SourceJobId { get; private set; }

    public Job SourceJob { get; private set; } = null!;

    public string Content { get; private set; } = "{}";

    public ScriptReviewStatus Status { get; private set; }

    public int Revision { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public static ReviewedScript Create(
        Guid contentProjectId,
        Guid sourceJobId,
        string canonicalContent,
        DateTimeOffset utcNow,
        Guid? id = null)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException("Content project ID cannot be empty.", nameof(contentProjectId));
        }

        if (sourceJobId == Guid.Empty)
        {
            throw new ArgumentException("Source job ID cannot be empty.", nameof(sourceJobId));
        }

        var scriptId = id ?? Guid.NewGuid();
        if (scriptId == Guid.Empty)
        {
            throw new ArgumentException("Reviewed script ID cannot be empty.", nameof(id));
        }

        return new ReviewedScript(
            scriptId,
            contentProjectId,
            sourceJobId,
            RequireJsonObject(canonicalContent, nameof(canonicalContent)),
            utcNow.ToUniversalTime());
    }

    public bool Edit(string canonicalContent, DateTimeOffset utcNow)
    {
        if (Status != ScriptReviewStatus.Draft)
        {
            throw new InvalidOperationException("An approved script cannot be edited.");
        }

        var normalizedContent = RequireJsonObject(canonicalContent, nameof(canonicalContent));
        if (string.Equals(Content, normalizedContent, StringComparison.Ordinal))
        {
            return false;
        }

        Content = normalizedContent;
        Revision++;
        UpdatedAt = NormalizeUpdateTime(utcNow);
        return true;
    }

    public bool Approve(DateTimeOffset utcNow)
    {
        if (Status == ScriptReviewStatus.Approved)
        {
            return false;
        }

        Status = ScriptReviewStatus.Approved;
        ApprovedAt = NormalizeUpdateTime(utcNow);
        UpdatedAt = ApprovedAt.Value;
        return true;
    }

    private DateTimeOffset NormalizeUpdateTime(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        return utcValue < UpdatedAt
            ? throw new ArgumentOutOfRangeException(
                nameof(value),
                "Update time cannot precede the previous update.")
            : utcValue;
    }

    private static string RequireJsonObject(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxContentLength)
        {
            throw new ArgumentException(
                $"Script content must contain 1 to {MaxContentLength} characters.",
                parameterName);
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Script content must be a JSON object.", parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Script content must be valid JSON.", parameterName, exception);
        }

        return value;
    }
}
