using AIStudio.Domain.Content;
using AIStudio.Domain.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable(
            "time_entries",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_time_entries_minutes",
                    "minutes > 0");
                table.HasCheckConstraint(
                    "ck_time_entries_kind",
                    "kind IN ('Human', 'Machine')");
            });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(entry => entry.ContentProjectId)
            .HasColumnName("content_project_id")
            .IsRequired();

        builder.Property(entry => entry.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(entry => entry.Category)
            .HasColumnName("category")
            .HasMaxLength(TrackingRules.MaxTokenLength)
            .IsRequired();

        builder.Property(entry => entry.Minutes)
            .HasColumnName("minutes")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(entry => entry.RecordedAt)
            .HasColumnName("recorded_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(entry => entry.Notes)
            .HasColumnName("notes")
            .HasMaxLength(TrackingRules.MaxNotesLength);

        builder.Property(entry => entry.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ContentProject>()
            .WithMany()
            .HasForeignKey(entry => entry.ContentProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => new { entry.ContentProjectId, entry.RecordedAt })
            .HasDatabaseName("ix_time_entries_content_project_id_recorded_at");
    }
}
