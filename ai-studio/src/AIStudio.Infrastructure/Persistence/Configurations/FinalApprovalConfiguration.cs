using AIStudio.Domain.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class FinalApprovalConfiguration : IEntityTypeConfiguration<FinalApproval>
{
    public void Configure(EntityTypeBuilder<FinalApproval> builder)
    {
        builder.ToTable(
            "final_approvals",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_final_approvals_revision",
                    "revision >= 1");
                table.HasCheckConstraint(
                    "ck_final_approvals_artifact_byte_size",
                    "artifact_byte_size > 0");
                table.HasCheckConstraint(
                    "ck_final_approvals_artifact_content_hash",
                    "char_length(artifact_content_hash) = 64");
                table.HasCheckConstraint(
                    "ck_final_approvals_decision",
                    "decision in ('approved', 'changes_requested')");
            });

        builder.HasKey(approval => approval.Id);

        builder.Property(approval => approval.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(approval => approval.ContentProjectId)
            .HasColumnName("content_project_id")
            .IsRequired();

        builder.Property(approval => approval.SourceRenderJobId)
            .HasColumnName("source_render_job_id")
            .IsRequired();

        builder.Property(approval => approval.ArtifactPath)
            .HasColumnName("artifact_path")
            .HasMaxLength(FinalApproval.MaxArtifactPathLength)
            .IsRequired();

        builder.Property(approval => approval.ArtifactContentHash)
            .HasColumnName("artifact_content_hash")
            .HasMaxLength(PublicationRules.ContentHashLength)
            .IsRequired();

        builder.Property(approval => approval.ArtifactByteSize)
            .HasColumnName("artifact_byte_size")
            .IsRequired();

        builder.Property(approval => approval.Destination)
            .HasColumnName("destination")
            .HasMaxLength(PublicationRules.MaxPlatformLength)
            .IsRequired();

        builder.Property(approval => approval.Notes)
            .HasColumnName("notes")
            .HasMaxLength(PublicationRules.MaxNotesLength);

        builder.Property(approval => approval.ApproverLabel)
            .HasColumnName("approver_label")
            .HasMaxLength(FinalApproval.MaxApproverLabelLength);

        builder.Property(approval => approval.Decision)
            .HasColumnName("decision")
            .HasMaxLength(FinalApprovalDecisionTokens.MaxTokenLength)
            .HasConversion(
                decision => FinalApprovalDecisionTokens.ToToken(decision),
                token => FinalApprovalDecisionTokens.Parse(token))
            .HasDefaultValue(FinalApprovalDecision.Approved)
            .IsRequired();

        builder.Property(approval => approval.DecisionNote)
            .HasColumnName("decision_note")
            .HasMaxLength(FinalApproval.MaxDecisionNoteLength);

        builder.Property(approval => approval.DecidedAt)
            .HasColumnName("decided_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(approval => approval.Revision)
            .HasColumnName("revision")
            .IsRequired();

        builder.Property(approval => approval.ApprovedAt)
            .HasColumnName("approved_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(approval => approval.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(approval => approval.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(approval => approval.ContentProject)
            .WithMany()
            .HasForeignKey(approval => approval.ContentProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(approval => approval.SourceRenderJob)
            .WithMany()
            .HasForeignKey(approval => approval.SourceRenderJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(approval => approval.ContentProjectId)
            .IsUnique()
            .HasDatabaseName("ux_final_approvals_content_project_id");

        builder.HasIndex(approval => approval.SourceRenderJobId)
            .HasDatabaseName("ix_final_approvals_source_render_job_id");
    }
}
