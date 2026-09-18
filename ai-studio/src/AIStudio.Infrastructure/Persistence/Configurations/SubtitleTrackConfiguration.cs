using AIStudio.Domain.Subtitles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class SubtitleTrackConfiguration : IEntityTypeConfiguration<SubtitleTrack>
{
    public void Configure(EntityTypeBuilder<SubtitleTrack> builder)
    {
        builder.ToTable(
            "subtitle_tracks",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_subtitle_tracks_byte_size",
                    "byte_size >= 0");
                table.HasCheckConstraint(
                    "ck_subtitle_tracks_content_hash",
                    "char_length(content_hash) = 64");
            });

        builder.HasKey(track => track.Id);

        builder.Property(track => track.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(track => track.ContentProjectId)
            .HasColumnName("content_project_id")
            .IsRequired();

        builder.Property(track => track.SourceJobId)
            .HasColumnName("source_job_id")
            .IsRequired();

        builder.Property(track => track.Path)
            .HasColumnName("path")
            .HasMaxLength(SubtitleTrack.MaxPathLength)
            .IsRequired();

        builder.Property(track => track.ByteSize)
            .HasColumnName("byte_size")
            .IsRequired();

        builder.Property(track => track.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(SubtitleTrack.ContentHashLength)
            .IsRequired();

        builder.Property(track => track.Origin)
            .HasColumnName("origin")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(track => track.Source)
            .HasColumnName("source")
            .HasMaxLength(SubtitleTrack.MaxProvenanceLength);

        builder.Property(track => track.Creator)
            .HasColumnName("creator")
            .HasMaxLength(SubtitleTrack.MaxProvenanceLength);

        builder.Property(track => track.License)
            .HasColumnName("license")
            .HasMaxLength(SubtitleTrack.MaxProvenanceLength);

        builder.Property(track => track.RetrievedAt)
            .HasColumnName("retrieved_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(track => track.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(track => track.ContentProject)
            .WithMany()
            .HasForeignKey(track => track.ContentProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(track => track.SourceJob)
            .WithMany()
            .HasForeignKey(track => track.SourceJobId)
            .OnDelete(DeleteBehavior.Restrict);

        // ponytail: one subtitle track per project; add per-render/versioned tracks if re-timing is required.
        builder.HasIndex(track => track.ContentProjectId)
            .IsUnique()
            .HasDatabaseName("ux_subtitle_tracks_content_project_id");

        builder.HasIndex(track => track.SourceJobId)
            .HasDatabaseName("ix_subtitle_tracks_source_job_id");
    }
}
