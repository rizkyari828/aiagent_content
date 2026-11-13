using AIStudio.Domain.Learning;
using AIStudio.Domain.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class ResearchPackageConfiguration : IEntityTypeConfiguration<ResearchPackageRecord>
{
    public void Configure(EntityTypeBuilder<ResearchPackageRecord> builder)
    {
        builder.ToTable("research_packages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.QueryTopic)
            .HasColumnName("query_topic")
            .HasMaxLength(ResearchPackageRecord.MaxTopicLength)
            .IsRequired();

        builder.Property(x => x.RegionCode)
            .HasColumnName("region_code")
            .HasMaxLength(ResearchPackageRecord.MaxRegionLength)
            .IsRequired();

        builder.Property(x => x.ProviderUsed)
            .HasColumnName("provider_used")
            .HasMaxLength(ResearchPackageRecord.MaxProviderLength)
            .IsRequired();

        builder.Property(x => x.CandidatesJson)
            .HasColumnName("candidates_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.PacingBlueprintJson)
            .HasColumnName("pacing_blueprint_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.WarningsJson)
            .HasColumnName("warnings_json")
            .HasColumnType("jsonb");

        builder.Property(x => x.DegradedFallback)
            .HasColumnName("degraded_fallback")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_research_packages_created_at");
    }
}

internal sealed class ResearchSourceConfiguration : IEntityTypeConfiguration<ResearchSourceRecord>
{
    public void Configure(EntityTypeBuilder<ResearchSourceRecord> builder)
    {
        builder.ToTable("research_sources");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.ResearchPackageId)
            .HasColumnName("research_package_id")
            .IsRequired();

        builder.Property(x => x.ContentProjectId)
            .HasColumnName("content_project_id");

        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(ResearchSourceRecord.MaxTitleLength)
            .IsRequired();

        builder.Property(x => x.Url)
            .HasColumnName("url")
            .HasMaxLength(ResearchSourceRecord.MaxUrlLength)
            .IsRequired();

        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasMaxLength(ResearchSourceRecord.MaxSourceTypeLength)
            .IsRequired();

        builder.Property(x => x.SnapshotHash)
            .HasColumnName("snapshot_hash")
            .HasMaxLength(ResearchSourceRecord.MaxHashLength)
            .IsRequired();

        builder.Property(x => x.RetrievedAt)
            .HasColumnName("retrieved_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ResearchPackageRecord>()
            .WithMany()
            .HasForeignKey(x => x.ResearchPackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ResearchPackageId)
            .HasDatabaseName("ix_research_sources_package_id");

        builder.HasIndex(x => x.ContentProjectId)
            .HasDatabaseName("ix_research_sources_project_id");
    }
}

internal sealed class EvidenceClaimConfiguration : IEntityTypeConfiguration<EvidenceClaimRecord>
{
    public void Configure(EntityTypeBuilder<EvidenceClaimRecord> builder)
    {
        builder.ToTable("evidence_claims");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.ResearchPackageId)
            .HasColumnName("research_package_id")
            .IsRequired();

        builder.Property(x => x.SourceId)
            .HasColumnName("source_id");

        builder.Property(x => x.ContentProjectId)
            .HasColumnName("content_project_id");

        builder.Property(x => x.ClaimText)
            .HasColumnName("claim_text")
            .HasMaxLength(EvidenceClaimRecord.MaxClaimLength)
            .IsRequired();

        builder.Property(x => x.ClaimKind)
            .HasColumnName("claim_kind")
            .HasMaxLength(EvidenceClaimRecord.MaxKindLength)
            .IsRequired();

        builder.Property(x => x.ConfidenceNote)
            .HasColumnName("confidence_note")
            .HasMaxLength(EvidenceClaimRecord.MaxNoteLength);

        builder.Property(x => x.VisualProofSuggestion)
            .HasColumnName("visual_proof_suggestion")
            .HasMaxLength(EvidenceClaimRecord.MaxVisualProofLength);

        builder.HasOne<ResearchPackageRecord>()
            .WithMany()
            .HasForeignKey(x => x.ResearchPackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ResearchSourceRecord>()
            .WithMany()
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ResearchPackageId)
            .HasDatabaseName("ix_evidence_claims_package_id");

        builder.HasIndex(x => x.ContentProjectId)
            .HasDatabaseName("ix_evidence_claims_project_id");
    }
}

internal sealed class LearningGuidelineConfiguration : IEntityTypeConfiguration<LearningGuideline>
{
    public void Configure(EntityTypeBuilder<LearningGuideline> builder)
    {
        builder.ToTable("learning_guidelines");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Kind)
            .HasColumnName("kind")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(LearningGuideline.MaxTitleLength)
            .IsRequired();

        builder.Property(x => x.Summary)
            .HasColumnName("summary")
            .HasMaxLength(LearningGuideline.MaxSummaryLength)
            .IsRequired();

        builder.Property(x => x.SourceCorrectionReason)
            .HasColumnName("source_correction_reason")
            .HasMaxLength(64);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.PromotedAt)
            .HasColumnName("promoted_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_learning_guidelines_status");
    }
}
