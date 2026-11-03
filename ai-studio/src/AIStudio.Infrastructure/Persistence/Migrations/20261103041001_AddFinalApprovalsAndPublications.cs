using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalApprovalsAndPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "final_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_render_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    artifact_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    artifact_byte_size = table.Column<long>(type: "bigint", nullable: false),
                    destination = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    approver_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_final_approvals", x => x.id);
                    table.CheckConstraint("ck_final_approvals_artifact_byte_size", "artifact_byte_size > 0");
                    table.CheckConstraint("ck_final_approvals_artifact_content_hash", "char_length(artifact_content_hash) = 64");
                    table.CheckConstraint("ck_final_approvals_revision", "revision >= 1");
                    table.ForeignKey(
                        name: "FK_final_approvals_content_projects_content_project_id",
                        column: x => x.content_project_id,
                        principalTable: "content_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_final_approvals_jobs_source_render_job_id",
                        column: x => x.source_render_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publication_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    final_approval_id = table.Column<Guid>(type: "uuid", nullable: false),
                    final_approval_revision = table.Column<int>(type: "integer", nullable: false),
                    artifact_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    video_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_records", x => x.id);
                    table.CheckConstraint("ck_publication_records_artifact_content_hash", "char_length(artifact_content_hash) = 64");
                    table.CheckConstraint("ck_publication_records_final_approval_revision", "final_approval_revision >= 1");
                    table.ForeignKey(
                        name: "FK_publication_records_content_projects_content_project_id",
                        column: x => x.content_project_id,
                        principalTable: "content_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publication_records_final_approvals_final_approval_id",
                        column: x => x.final_approval_id,
                        principalTable: "final_approvals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_final_approvals_source_render_job_id",
                table: "final_approvals",
                column: "source_render_job_id");

            migrationBuilder.CreateIndex(
                name: "ux_final_approvals_content_project_id",
                table: "final_approvals",
                column: "content_project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publication_records_final_approval_id",
                table: "publication_records",
                column: "final_approval_id");

            migrationBuilder.CreateIndex(
                name: "ux_publication_records_content_project_id",
                table: "publication_records",
                column: "content_project_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publication_records");

            migrationBuilder.DropTable(
                name: "final_approvals");
        }
    }
}
