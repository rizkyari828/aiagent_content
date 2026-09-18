using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scene_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scene_index = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    creator = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    license = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scene_assets", x => x.id);
                    table.CheckConstraint("ck_scene_assets_byte_size", "byte_size >= 0");
                    table.CheckConstraint("ck_scene_assets_content_hash", "char_length(content_hash) = 64");
                    table.CheckConstraint("ck_scene_assets_scene_index", "scene_index >= 0");
                    table.ForeignKey(
                        name: "FK_scene_assets_content_projects_content_project_id",
                        column: x => x.content_project_id,
                        principalTable: "content_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scene_assets_jobs_source_job_id",
                        column: x => x.source_job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scene_assets_source_job_id",
                table: "scene_assets",
                column: "source_job_id");

            migrationBuilder.CreateIndex(
                name: "ux_scene_assets_content_project_scene",
                table: "scene_assets",
                columns: new[] { "content_project_id", "scene_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scene_assets");
        }
    }
}
