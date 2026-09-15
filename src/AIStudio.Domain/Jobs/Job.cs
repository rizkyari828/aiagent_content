using System.Text.Json;
using AIStudio.Domain.Content;

namespace AIStudio.Domain.Jobs;

public sealed class Job
{
    public const int MaxInputVersionHashLength = 128;
    public const int MaxWorkerIdLength = 200;
    public const int MaxErrorCodeLength = 100;
    public const int MaxErrorSummaryLength = 2_000;
    public const int MaximumAllowedRetries = 10;

    private Job()
    {
    }

    private Job(
        Guid id,
        Guid contentProjectId,
        JobType type,
        string inputVersionHash,
        string payload,
        int maxRetries,
        DateTimeOffset createdAt)
    {
        Id = id;
        ContentProjectId = contentProjectId;
        Type = type;
        InputVersionHash = inputVersionHash;
        Payload = payload;
        MaxRetries = maxRetries;
        Status = JobStatus.Queued;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ContentProjectId { get; private set; }

    public ContentProject ContentProject { get; private set; } = null!;

    public JobType Type { get; private set; }

    public JobStatus Status { get; private set; }

    public string InputVersionHash { get; private set; } = string.Empty;

    public int RetryCount { get; private set; }

    public int MaxRetries { get; private set; }

    public string Payload { get; private set; } = "{}";

    public string? Result { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorSummary { get; private set; }

    public string? WorkerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public static Job Create(
        Guid contentProjectId,
        JobType type,
        string inputVersionHash,
        string payload,
        DateTimeOffset utcNow,
        int maxRetries = 2,
        Guid? id = null)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException("Content project ID cannot be empty.", nameof(contentProjectId));
        }

        var jobId = id ?? Guid.NewGuid();
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Job ID cannot be empty.", nameof(id));
        }

        if (maxRetries is < 0 or > MaximumAllowedRetries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRetries),
                $"Max retries must be between 0 and {MaximumAllowedRetries}.");
        }

        return new Job(
            jobId,
            contentProjectId,
            type,
            RequireText(inputVersionHash, MaxInputVersionHashLength, nameof(inputVersionHash)),
            RequireJsonObject(payload, nameof(payload)),
            maxRetries,
            utcNow.ToUniversalTime());
    }

    public void Start(
        string workerId,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset utcNow)
    {
        EnsureStatus(JobStatus.Queued);

        var normalizedNow = NormalizeUpdateTime(utcNow);
        var normalizedLease = leaseExpiresAt.ToUniversalTime();
        if (normalizedLease <= normalizedNow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAt),
                "Lease expiry must be later than the start time.");
        }

        WorkerId = RequireText(workerId, MaxWorkerIdLength, nameof(workerId));
        Status = JobStatus.Running;
        StartedAt = normalizedNow;
        LeaseExpiresAt = normalizedLease;
        UpdatedAt = normalizedNow;
    }

    public void Succeed(string result, DateTimeOffset utcNow)
    {
        EnsureStatus(JobStatus.Running);

        var completedAt = NormalizeUpdateTime(utcNow);
        Result = RequireJsonObject(result, nameof(result));
        ErrorCode = null;
        ErrorSummary = null;
        Status = JobStatus.Succeeded;
        CompletedAt = completedAt;
        LeaseExpiresAt = null;
        UpdatedAt = completedAt;
    }

    public void Fail(
        string errorCode,
        string errorSummary,
        DateTimeOffset utcNow)
    {
        EnsureStatus(JobStatus.Running);

        var completedAt = NormalizeUpdateTime(utcNow);
        ErrorCode = RequireText(errorCode, MaxErrorCodeLength, nameof(errorCode));
        ErrorSummary = RequireText(errorSummary, MaxErrorSummaryLength, nameof(errorSummary));
        Result = null;
        Status = JobStatus.Failed;
        CompletedAt = completedAt;
        LeaseExpiresAt = null;
        UpdatedAt = completedAt;
    }

    public void Cancel(DateTimeOffset utcNow)
    {
        if (Status is not (JobStatus.Queued or JobStatus.Running))
        {
            throw new InvalidOperationException($"Cannot cancel a job in {Status} status.");
        }

        var completedAt = NormalizeUpdateTime(utcNow);
        Status = JobStatus.Cancelled;
        CompletedAt = completedAt;
        LeaseExpiresAt = null;
        UpdatedAt = completedAt;
    }

    public void RequeueForRetry(DateTimeOffset utcNow)
    {
        EnsureStatus(JobStatus.Failed);

        if (RetryCount >= MaxRetries)
        {
            throw new InvalidOperationException("The job has exhausted its retry allowance.");
        }

        var updatedAt = NormalizeUpdateTime(utcNow);
        RetryCount++;
        Status = JobStatus.Queued;
        StartedAt = null;
        CompletedAt = null;
        LeaseExpiresAt = null;
        WorkerId = null;
        ErrorCode = null;
        ErrorSummary = null;
        Result = null;
        UpdatedAt = updatedAt;
    }

    private void EnsureStatus(JobStatus requiredStatus)
    {
        if (Status != requiredStatus)
        {
            throw new InvalidOperationException(
                $"Job must be {requiredStatus}; current status is {Status}.");
        }
    }

    private DateTimeOffset NormalizeUpdateTime(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        return utcValue < CreatedAt
            ? throw new ArgumentOutOfRangeException(
                nameof(value),
                "Update time cannot precede creation.")
            : utcValue;
    }

    private static string RequireText(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim()
            ?? throw new ArgumentNullException(parameterName);

        return normalized.Length == 0 || normalized.Length > maxLength
            ? throw new ArgumentException(
                $"Value must contain 1 to {maxLength} characters.",
                parameterName)
            : normalized;
    }

    private static string RequireJsonObject(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("JSON value is required.", parameterName);
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("JSON value must be an object.", parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("JSON value must be valid.", parameterName, exception);
        }

        return value;
    }
}
