using AIStudio.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class SceneAssetConfiguration : IEntityTypeConfiguration<SceneAsset>
{
    public void Configure(EntityTypeBuilder<SceneAsset> builder)
    {
        builder.ToTable(
            "scene_assets",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_scene_assets_scene_index",
                    "scene_index >= 0");
                table.HasCheckConstraint(
                    "ck_scene_assets_byte_size",
                    "byte_size >= 0");
                table.HasCheckConstraint(
                    "ck_scene_assets_content_hash",
                    "char_length(content_hash) = 64");
            });

        builder.HasKey(asset => asset.Id);

        builder.Property(asset => asset.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(asset => asset.ContentProjectId)
            .HasColumnName("content_project_id")
            .IsRequired();

        builder.Property(asset => asset.SourceJobId)
            .HasColumnName("source_job_id")
            .IsRequired();

        builder.Property(asset => asset.SceneIndex)
            .HasColumnName("scene_index")
            .IsRequired();

        builder.Property(asset => asset.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(asset => asset.Path)
            .HasColumnName("path")
            .HasMaxLength(SceneAsset.MaxPathLength)
            .IsRequired();

        builder.Property(asset => asset.ByteSize)
            .HasColumnName("byte_size")
            .IsRequired();

        builder.Property(asset => asset.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(SceneAsset.ContentHashLength)
            .IsRequired();

        builder.Property(asset => asset.Origin)
            .HasColumnName("origin")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(asset => asset.Source)
            .HasColumnName("source")
            .HasMaxLength(SceneAsset.MaxProvenanceLength);

        builder.Property(asset => asset.Creator)
            .HasColumnName("creator")
            .HasMaxLength(SceneAsset.MaxProvenanceLength);

        builder.Property(asset => asset.License)
            .HasColumnName("license")
            .HasMaxLength(SceneAsset.MaxProvenanceLength);

        builder.Property(asset => asset.RetrievedAt)
            .HasColumnName("retrieved_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(asset => asset.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(asset => asset.ContentProject)
            .WithMany()
            .HasForeignKey(asset => asset.ContentProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(asset => asset.SourceJob)
            .WithMany()
            .HasForeignKey(asset => asset.SourceJobId)
            .OnDelete(DeleteBehavior.Restrict);

        // ponytail: one asset per scene; add versioning/replacement when re-collection is required.
        builder.HasIndex(asset => new { asset.ContentProjectId, asset.SceneIndex })
            .IsUnique()
            .HasDatabaseName("ux_scene_assets_content_project_scene");

        builder.HasIndex(asset => asset.SourceJobId)
            .HasDatabaseName("ix_scene_assets_source_job_id");
    }
}
