using System.Data;
using System.Text.Json;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace AIStudio.Infrastructure.Jobs;

public sealed class PostgreSqlJobQueue(
    IDbContextFactory<Persistence.ApplicationDbContext> contextFactory) : IJobQueue
{
    private const string ClaimSql = """
        WITH exhausted AS (
            UPDATE jobs
            SET status = 'Failed',
                completed_at = @now,
                updated_at = @now,
                lease_expires_at = NULL,
                error_code = 'lease_expired',
                error_summary = 'Worker lease expired and retry allowance was exhausted.'
            WHERE status = 'Running'
              AND lease_expires_at IS NOT NULL
              AND lease_expires_at <= @now
              AND retry_count >= max_retries
            RETURNING id
        ),
        candidate AS MATERIALIZED (
            SELECT id, status AS previous_status
            FROM jobs
            WHERE status = 'Queued'
               OR (
                    status = 'Running'
                    AND lease_expires_at IS NOT NULL
                    AND lease_expires_at <= @now
                    AND retry_count < max_retries
               )
            ORDER BY created_at, id
            FOR UPDATE SKIP LOCKED
            LIMIT 1
        ),
        claimed AS (
            UPDATE jobs AS job
            SET status = 'Running',
                retry_count = CASE
                    WHEN candidate.previous_status = 'Running'
                    THEN job.retry_count + 1
                    ELSE job.retry_count
                END,
                worker_id = @worker_id,
                started_at = @now,
                completed_at = NULL,
                lease_expires_at = @lease_expires_at,
                updated_at = @now,
                result = NULL
            FROM candidate
            WHERE job.id = candidate.id
            RETURNING
                job.id,
                job.content_project_id,
                job.job_type,
                job.input_version_hash,
                job.payload,
                job.retry_count,
                job.max_retries,
                candidate.previous_status = 'Running' AS recovered_from_expired_lease
        )
        SELECT
            id,
            content_project_id,
            job_type,
            input_version_hash,
            payload::text,
            retry_count,
            max_retries,
            recovered_from_expired_lease
        FROM claimed;
        """;

    public async Task<ClaimedJob?> ClaimNextAsync(
        string workerId,
        DateTimeOffset utcNow,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        ValidateWorkerAndLease(workerId, utcNow, leaseExpiresAt);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await using var command = new NpgsqlCommand(ClaimSql, connection, transaction);

        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, utcNow.ToUniversalTime());
        command.Parameters.AddWithValue(
            "lease_expires_at",
            NpgsqlDbType.TimestampTz,
            leaseExpiresAt.ToUniversalTime());
        command.Parameters.AddWithValue("worker_id", NpgsqlDbType.Text, workerId);

        ClaimedJob? claimedJob = null;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                claimedJob = new ClaimedJob(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    Enum.Parse<JobType>(reader.GetString(2)),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetBoolean(7));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return claimedJob;
    }

    public async Task<bool> CompleteAsync(
        Guid jobId,
        string workerId,
        string result,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ValidateOwnership(jobId, workerId);
        ValidateJsonObject(result, nameof(result));

        const string sql = """
            UPDATE jobs
            SET status = 'Succeeded',
                result = @result,
                error_code = NULL,
                error_summary = NULL,
                completed_at = @now,
                lease_expires_at = NULL,
                updated_at = @now
            WHERE id = @job_id
              AND status = 'Running'
              AND worker_id = @worker_id;
            """;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, jobId);
        command.Parameters.AddWithValue("worker_id", NpgsqlDbType.Text, workerId);
        command.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, result);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, utcNow.ToUniversalTime());

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<JobFailureResult?> FailAsync(
        Guid jobId,
        string workerId,
        string errorCode,
        string errorSummary,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ValidateOwnership(jobId, workerId);
        errorCode = RequireText(errorCode, Job.MaxErrorCodeLength, nameof(errorCode));
        errorSummary = RequireText(errorSummary, Job.MaxErrorSummaryLength, nameof(errorSummary));

        const string sql = """
            UPDATE jobs
            SET status = CASE
                    WHEN retry_count < max_retries THEN 'Queued'
                    ELSE 'Failed'
                END,
                retry_count = CASE
                    WHEN retry_count < max_retries THEN retry_count + 1
                    ELSE retry_count
                END,
                worker_id = CASE
                    WHEN retry_count < max_retries THEN NULL
                    ELSE worker_id
                END,
                started_at = CASE
                    WHEN retry_count < max_retries THEN NULL
                    ELSE started_at
                END,
                completed_at = CASE
                    WHEN retry_count < max_retries THEN NULL
                    ELSE @now
                END,
                lease_expires_at = NULL,
                result = NULL,
                error_code = @error_code,
                error_summary = @error_summary,
                updated_at = @now
            WHERE id = @job_id
              AND status = 'Running'
              AND worker_id = @worker_id
            RETURNING status, retry_count;
            """;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, jobId);
        command.Parameters.AddWithValue("worker_id", NpgsqlDbType.Text, workerId);
        command.Parameters.AddWithValue("error_code", NpgsqlDbType.Text, errorCode);
        command.Parameters.AddWithValue("error_summary", NpgsqlDbType.Text, errorSummary);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, utcNow.ToUniversalTime());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var status = Enum.Parse<JobStatus>(reader.GetString(0));
        var retryCount = reader.GetInt32(1);
        return new JobFailureResult(status, retryCount, status == JobStatus.Queued);
    }

    public async Task<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        DateTimeOffset utcNow,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        ValidateOwnership(jobId, workerId);
        ValidateWorkerAndLease(workerId, utcNow, leaseExpiresAt);

        const string sql = """
            UPDATE jobs
            SET lease_expires_at = @lease_expires_at,
                updated_at = @now
            WHERE id = @job_id
              AND status = 'Running'
              AND worker_id = @worker_id;
            """;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Uuid, jobId);
        command.Parameters.AddWithValue("worker_id", NpgsqlDbType.Text, workerId);
        command.Parameters.AddWithValue(
            "lease_expires_at",
            NpgsqlDbType.TimestampTz,
            leaseExpiresAt.ToUniversalTime());
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, utcNow.ToUniversalTime());

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void ValidateWorkerAndLease(
        string workerId,
        DateTimeOffset utcNow,
        DateTimeOffset leaseExpiresAt)
    {
        RequireText(workerId, Job.MaxWorkerIdLength, nameof(workerId));
        if (leaseExpiresAt.ToUniversalTime() <= utcNow.ToUniversalTime())
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAt),
                "Lease expiry must be later than the claim time.");
        }
    }

    private static void ValidateOwnership(Guid jobId, string workerId)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Job ID cannot be empty.", nameof(jobId));
        }

        RequireText(workerId, Job.MaxWorkerIdLength, nameof(workerId));
    }

    private static string RequireText(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim() ?? throw new ArgumentNullException(parameterName);
        return normalized.Length == 0 || normalized.Length > maxLength
            ? throw new ArgumentException(
                $"Value must contain 1 to {maxLength} characters.",
                parameterName)
            : normalized;
    }

    private static void ValidateJsonObject(string value, string parameterName)
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
    }
}
