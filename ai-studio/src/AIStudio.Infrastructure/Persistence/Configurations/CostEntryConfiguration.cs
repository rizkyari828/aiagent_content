using AIStudio.Domain.Content;
using AIStudio.Domain.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIStudio.Infrastructure.Persistence.Configurations;

internal sealed class CostEntryConfiguration : IEntityTypeConfiguration<CostEntry>
{
    public void Configure(EntityTypeBuilder<CostEntry> builder)
    {
        builder.ToTable(
            "cost_entries",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_cost_entries_amount",
                    "amount > 0");
                table.HasCheckConstraint(
                    "ck_cost_entries_kind",
                    "kind IN ('Actual', 'Estimated', 'Refunded')");
                table.HasCheckConstraint(
                    "ck_cost_entries_currency",
                    "char_length(currency) = 3");
            });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(entry => entry.ContentProjectId)
            .HasColumnName("content_project_id")
            .IsRequired();

        builder.Property(entry => entry.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(entry => entry.Currency)
            .HasColumnName("currency")
            .HasMaxLength(TrackingRules.CurrencyLength)
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

        builder.Property(entry => entry.IncurredAt)
            .HasColumnName("incurred_at")
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

        builder.HasIndex(entry => new { entry.ContentProjectId, entry.IncurredAt })
            .HasDatabaseName("ix_cost_entries_content_project_id_incurred_at");
    }
}
