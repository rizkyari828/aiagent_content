using AIStudio.Domain.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class PublicationRecordConfiguration
    : IEntityTypeConfiguration<PublicationRecord>
{
    public void Configure(EntityTypeBuilder<PublicationRecord> builder)
    {
        builder.ToTable(
            "publication_records",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_publication_records_final_approval_revision",
                    "final_approval_revision >= 1");
                table.HasCheckConstraint(
                    "ck_publication_records_artifact_content_hash",
                    "char_length(artifact_content_hash) = 64");
            });

        builder.HasKey(record => record.Id);

        builder.Property(record => record.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(record => record.ContentProjectId)
            .HasColumnName("content_project_id")
            .IsRequired();

        builder.Property(record => record.FinalApprovalId)
            .HasColumnName("final_approval_id")
            .IsRequired();

        builder.Property(record => record.FinalApprovalRevision)
            .HasColumnName("final_approval_revision")
            .IsRequired();

        builder.Property(record => record.ArtifactContentHash)
            .HasColumnName("artifact_content_hash")
            .HasMaxLength(PublicationRules.ContentHashLength)
            .IsRequired();

        builder.Property(record => record.Platform)
            .HasColumnName("platform")
            .HasMaxLength(PublicationRules.MaxPlatformLength)
            .IsRequired();

        builder.Property(record => record.Url)
            .HasColumnName("url")
            .HasMaxLength(PublicationRules.MaxUrlLength)
            .IsRequired();

        builder.Property(record => record.VideoId)
            .HasColumnName("video_id")
            .HasMaxLength(PublicationRules.MaxVideoIdLength)
            .IsRequired();

        builder.Property(record => record.PublishedAt)
            .HasColumnName("published_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(record => record.Notes)
            .HasColumnName("notes")
            .HasMaxLength(PublicationRules.MaxNotesLength);

        builder.Property(record => record.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(record => record.ContentProject)
            .WithMany()
            .HasForeignKey(record => record.ContentProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(record => record.FinalApproval)
            .WithMany()
            .HasForeignKey(record => record.FinalApprovalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(record => record.ContentProjectId)
            .IsUnique()
            .HasDatabaseName("ux_publication_records_content_project_id");

        builder.HasIndex(record => record.FinalApprovalId)
            .HasDatabaseName("ix_publication_records_final_approval_id");
    }
}
