using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewedScripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reviewed_scripts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviewed_scripts", x => x.id);
                    table.CheckConstraint("ck_reviewed_scripts_revision", "revision >= 1");
                    table.ForeignKey(
                        name: "FK_reviewed_scripts_content_projects_content_project_id",
                        column: x => x.content_project_id,
                        principalTable: "content_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reviewed_scripts_jobs_source_job_id",
                        column: x => x.source_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_reviewed_scripts_content_project_id",
                table: "reviewed_scripts",
                column: "content_project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_reviewed_scripts_source_job_id",
                table: "reviewed_scripts",
                column: "source_job_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reviewed_scripts");
        }
    }
}
