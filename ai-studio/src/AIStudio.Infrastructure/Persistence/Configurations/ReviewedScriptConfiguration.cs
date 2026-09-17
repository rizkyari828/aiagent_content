using AIStudio.Domain.Scripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class ReviewedScriptConfiguration : IEntityTypeConfiguration<ReviewedScript>
{
    public void Configure(EntityTypeBuilder<ReviewedScript> builder)
    {
        builder.ToTable(
            "reviewed_scripts",
            table => table.HasCheckConstraint(
                "ck_reviewed_scripts_revision",
                "revision >= 1"));

        builder.HasKey(script => script.Id);

        builder.Property(script => script.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(script => script.ContentProjectId)
            .HasColumnName("content_project_id")
            .IsRequired();

        builder.Property(script => script.SourceJobId)
            .HasColumnName("source_job_id")
            .IsRequired();

        builder.Property(script => script.Content)
            .HasColumnName("content")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(script => script.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(script => script.Revision)
            .HasColumnName("revision")
            .IsRequired();

        builder.Property(script => script.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(script => script.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(script => script.ApprovedAt)
            .HasColumnName("approved_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne(script => script.ContentProject)
            .WithMany()
            .HasForeignKey(script => script.ContentProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(script => script.SourceJob)
            .WithMany()
            .HasForeignKey(script => script.SourceJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(script => script.ContentProjectId)
            .IsUnique()
            .HasDatabaseName("ux_reviewed_scripts_content_project_id");

        builder.HasIndex(script => script.SourceJobId)
            .IsUnique()
            .HasDatabaseName("ux_reviewed_scripts_source_job_id");
    }
}
