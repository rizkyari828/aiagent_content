using AIStudio.Domain.Narration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class NarrationTrackConfiguration : IEntityTypeConfiguration<NarrationTrack>
{
    public void Configure(EntityTypeBuilder<NarrationTrack> builder)
    {
        builder.ToTable(
            "narration_tracks",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_narration_tracks_byte_size",
                    "byte_size >= 0");
                table.HasCheckConstraint(
                    "ck_narration_tracks_content_hash",
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
            .HasMaxLength(NarrationTrack.MaxPathLength)
            .IsRequired();

        builder.Property(track => track.ByteSize)
            .HasColumnName("byte_size")
            .IsRequired();

        builder.Property(track => track.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(NarrationTrack.ContentHashLength)
            .IsRequired();

        builder.Property(track => track.Origin)
            .HasColumnName("origin")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(track => track.Source)
            .HasColumnName("source")
            .HasMaxLength(NarrationTrack.MaxProvenanceLength);

        builder.Property(track => track.Creator)
            .HasColumnName("creator")
            .HasMaxLength(NarrationTrack.MaxProvenanceLength);

        builder.Property(track => track.License)
            .HasColumnName("license")
            .HasMaxLength(NarrationTrack.MaxProvenanceLength);

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

        // ponytail: one narration track per project; add per-scene/timing tracks when subtitle alignment requires it.
        builder.HasIndex(track => track.ContentProjectId)
            .IsUnique()
            .HasDatabaseName("ux_narration_tracks_content_project_id");

        builder.HasIndex(track => track.SourceJobId)
            .HasDatabaseName("ix_narration_tracks_source_job_id");
    }
}
