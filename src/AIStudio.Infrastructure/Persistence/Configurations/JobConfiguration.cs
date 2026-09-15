using AIStudio.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable(
            "jobs",
            table =>
            {
                table.HasCheckConstraint("ck_jobs_retry_count", "retry_count >= 0");
                table.HasCheckConstraint(
                    "ck_jobs_max_retries",
                    "max_retries >= 0 AND max_retries <= 10");
                table.HasCheckConstraint(
                    "ck_jobs_retry_allowance",
                    "retry_count <= max_retries");
            });

        builder.HasKey(job => job.Id);

        builder.Property(job => job.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(job => job.ContentProjectId)
            .HasColumnName("content_project_id")
            .IsRequired();

        builder.Property(job => job.Type)
            .HasColumnName("job_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(job => job.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(job => job.InputVersionHash)
            .HasColumnName("input_version_hash")
            .HasMaxLength(Job.MaxInputVersionHashLength)
            .IsRequired();

        builder.Property(job => job.RetryCount)
            .HasColumnName("retry_count")
            .IsRequired();

        builder.Property(job => job.MaxRetries)
            .HasColumnName("max_retries")
            .IsRequired();

        builder.Property(job => job.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(job => job.Result)
            .HasColumnName("result")
            .HasColumnType("jsonb");

        builder.Property(job => job.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(Job.MaxErrorCodeLength);

        builder.Property(job => job.ErrorSummary)
            .HasColumnName("error_summary")
            .HasMaxLength(Job.MaxErrorSummaryLength);

        builder.Property(job => job.WorkerId)
            .HasColumnName("worker_id")
            .HasMaxLength(Job.MaxWorkerIdLength);

        builder.Property(job => job.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(job => job.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(job => job.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(job => job.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(job => job.LeaseExpiresAt)
            .HasColumnName("lease_expires_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne(job => job.ContentProject)
            .WithMany()
            .HasForeignKey(job => job.ContentProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(job => job.ContentProjectId)
            .HasDatabaseName("ix_jobs_content_project_id");

        builder.HasIndex(job => new { job.Status, job.CreatedAt })
            .HasDatabaseName("ix_jobs_status_created_at");
    }
}
