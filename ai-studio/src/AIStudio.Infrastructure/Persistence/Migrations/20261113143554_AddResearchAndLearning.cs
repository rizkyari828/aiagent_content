using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchAndLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_correction_reason",
                table: "reviewed_scripts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "packaging_json",
                table: "reviewed_scripts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_research_package_id",
                table: "content_projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "learning_guidelines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    source_correction_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    promoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_guidelines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "research_packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_topic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    region_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_used = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    candidates_json = table.Column<string>(type: "jsonb", nullable: false),
                    pacing_blueprint_json = table.Column<string>(type: "jsonb", nullable: true),
                    warnings_json = table.Column<string>(type: "jsonb", nullable: true),
                    degraded_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_packages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "research_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_sources", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_sources_research_packages_research_package_id",
                        column: x => x.research_package_id,
                        principalTable: "research_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidence_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content_project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claim_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    claim_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    confidence_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    visual_proof_suggestion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_evidence_claims_research_packages_research_package_id",
                        column: x => x.research_package_id,
                        principalTable: "research_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_evidence_claims_research_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "research_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_claims_package_id",
                table: "evidence_claims",
                column: "research_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_claims_project_id",
                table: "evidence_claims",
                column: "content_project_id");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_claims_source_id",
                table: "evidence_claims",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_learning_guidelines_status",
                table: "learning_guidelines",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_research_packages_created_at",
                table: "research_packages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_research_sources_package_id",
                table: "research_sources",
                column: "research_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_research_sources_project_id",
                table: "research_sources",
                column: "content_project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidence_claims");

            migrationBuilder.DropTable(
                name: "learning_guidelines");

            migrationBuilder.DropTable(
                name: "research_sources");

            migrationBuilder.DropTable(
                name: "research_packages");

            migrationBuilder.DropColumn(
                name: "last_correction_reason",
                table: "reviewed_scripts");

            migrationBuilder.DropColumn(
                name: "packaging_json",
                table: "reviewed_scripts");

            migrationBuilder.DropColumn(
                name: "source_research_package_id",
                table: "content_projects");
        }
    }
}
