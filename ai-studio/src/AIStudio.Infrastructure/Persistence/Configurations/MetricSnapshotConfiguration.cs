using AIStudio.Domain.Approvals;
using AIStudio.Domain.Content;
using AIStudio.Domain.Tracking;
using AIStudio.Infrastructure.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class MetricSnapshotConfiguration
    : IEntityTypeConfiguration<MetricSnapshot>
{
    public void Configure(EntityTypeBuilder<MetricSnapshot> builder)
    {
        builder.ToTable(
            "metric_snapshots",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_metric_snapshots_non_negative",
                    "(views IS NULL OR views >= 0) AND (impressions IS NULL OR impressions >= 0) AND (average_view_duration_seconds IS NULL OR average_view_duration_seconds >= 0) AND (watch_time_minutes IS NULL OR watch_time_minutes >= 0) AND (subscribers_gained IS NULL OR subscribers_gained >= 0)");
                table.HasCheckConstraint(
                    "ck_metric_snapshots_click_through_rate",
                    "click_through_rate IS NULL OR (click_through_rate >= 0 AND click_through_rate <= 1)");
            });

        builder.HasKey(snapshot => snapshot.Id);

        builder.Property(snapshot => snapshot.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(snapshot => snapshot.ContentProjectId)
            .HasColumnName("content_project_id")
            .IsRequired();

        builder.Property(snapshot => snapshot.PublicationRecordId)
            .HasColumnName("publication_record_id");

        builder.Property(snapshot => snapshot.Views)
            .HasColumnName("views");

        builder.Property(snapshot => snapshot.Impressions)
            .HasColumnName("impressions");

        builder.Property(snapshot => snapshot.ClickThroughRate)
            .HasColumnName("click_through_rate")
            .HasPrecision(9, 6);

        builder.Property(snapshot => snapshot.AverageViewDurationSeconds)
            .HasColumnName("average_view_duration_seconds")
            .HasPrecision(12, 3);

        builder.Property(snapshot => snapshot.WatchTimeMinutes)
            .HasColumnName("watch_time_minutes")
            .HasPrecision(14, 3);

        builder.Property(snapshot => snapshot.SubscribersGained)
            .HasColumnName("subscribers_gained");

        builder.Property(snapshot => snapshot.CapturedAt)
            .HasColumnName("captured_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(snapshot => snapshot.Source)
            .HasColumnName("source")
            .HasMaxLength(TrackingRules.MaxTokenLength)
            .IsRequired();

        builder.Property(snapshot => snapshot.Notes)
            .HasColumnName("notes")
            .HasMaxLength(TrackingRules.MaxNotesLength);

        builder.Property(snapshot => snapshot.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ContentProject>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.ContentProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PublicationRecord>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.PublicationRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(snapshot => new { snapshot.ContentProjectId, snapshot.CapturedAt })
            .IsUnique()
            .HasDatabaseName(TrackingRepository.MetricSnapshotUniqueIndex);

        builder.HasIndex(snapshot => snapshot.PublicationRecordId)
            .HasDatabaseName("ix_metric_snapshots_publication_record_id");
    }
}
