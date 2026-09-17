using AIStudio.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class ContentProjectConfiguration : IEntityTypeConfiguration<ContentProject>
{
    public void Configure(EntityTypeBuilder<ContentProject> builder)
    {
        builder.ToTable("content_projects");

        builder.HasKey(project => project.Id);

        builder.Property(project => project.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(project => project.Title)
            .HasColumnName("title")
            .HasMaxLength(ContentProject.MaxTitleLength)
            .IsRequired();

        builder.Property(project => project.Brief)
            .HasColumnName("brief")
            .HasMaxLength(ContentProject.MaxBriefLength);

        builder.Property(project => project.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(project => project.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(project => project.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(project => project.Status)
            .HasDatabaseName("ix_content_projects_status");
    }
}
