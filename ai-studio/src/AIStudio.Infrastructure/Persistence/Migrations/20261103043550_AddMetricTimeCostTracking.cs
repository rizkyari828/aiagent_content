using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMetricTimeCostTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cost_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    incurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cost_entries", x => x.id);
                    table.CheckConstraint("ck_cost_entries_amount", "amount > 0");
                    table.CheckConstraint("ck_cost_entries_currency", "char_length(currency) = 3");
                    table.CheckConstraint("ck_cost_entries_kind", "kind IN ('Actual', 'Estimated', 'Refunded')");
                    table.ForeignKey(
                        name: "FK_cost_entries_content_projects_content_project_id",
                        column: x => x.content_project_id,
                        principalTable: "content_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metric_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    publication_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    views = table.Column<int>(type: "integer", nullable: true),
                    impressions = table.Column<int>(type: "integer", nullable: true),
                    click_through_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    average_view_duration_seconds = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    watch_time_minutes = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
                    subscribers_gained = table.Column<int>(type: "integer", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metric_snapshots", x => x.id);
                    table.CheckConstraint("ck_metric_snapshots_click_through_rate", "click_through_rate IS NULL OR (click_through_rate >= 0 AND click_through_rate <= 1)");
                    table.CheckConstraint("ck_metric_snapshots_non_negative", "(views IS NULL OR views >= 0) AND (impressions IS NULL OR impressions >= 0) AND (average_view_duration_seconds IS NULL OR average_view_duration_seconds >= 0) AND (watch_time_minutes IS NULL OR watch_time_minutes >= 0) AND (subscribers_gained IS NULL OR subscribers_gained >= 0)");
                    table.ForeignKey(
                        name: "FK_metric_snapshots_content_projects_content_project_id",
                        column: x => x.content_project_id,
                        principalTable: "content_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_metric_snapshots_publication_records_publication_record_id",
                        column: x => x.publication_record_id,
                        principalTable: "publication_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "time_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    minutes = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_entries", x => x.id);
                    table.CheckConstraint("ck_time_entries_kind", "kind IN ('Human', 'Machine')");
                    table.CheckConstraint("ck_time_entries_minutes", "minutes > 0");
                    table.ForeignKey(
                        name: "FK_time_entries_content_projects_content_project_id",
                        column: x => x.content_project_id,
                        principalTable: "content_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cost_entries_content_project_id_incurred_at",
                table: "cost_entries",
                columns: new[] { "content_project_id", "incurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_metric_snapshots_publication_record_id",
                table: "metric_snapshots",
                column: "publication_record_id");

            migrationBuilder.CreateIndex(
                name: "ux_metric_snapshots_content_project_id_captured_at",
                table: "metric_snapshots",
                columns: new[] { "content_project_id", "captured_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_content_project_id_recorded_at",
                table: "time_entries",
                columns: new[] { "content_project_id", "recorded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cost_entries");

            migrationBuilder.DropTable(
                name: "metric_snapshots");

            migrationBuilder.DropTable(
                name: "time_entries");
        }
    }
}
