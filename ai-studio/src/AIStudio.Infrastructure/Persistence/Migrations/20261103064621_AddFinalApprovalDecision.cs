using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalApprovalDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "decision",
                table: "final_approvals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "approved");

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "final_approvals",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // Existing rows predate the two-way decision. They were approvals, so the
            // decision default ('approved') is correct, and their decision time is the
            // moment they were approved. The column is added nullable, backfilled, then
            // tightened so no legacy row receives a placeholder timestamp.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "final_approvals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE final_approvals SET decided_at = approved_at WHERE decided_at IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "decided_at",
                table: "final_approvals",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_final_approvals_decision",
                table: "final_approvals",
                sql: "decision in ('approved', 'changes_requested')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_final_approvals_decision",
                table: "final_approvals");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "final_approvals");

            migrationBuilder.DropColumn(
                name: "decision",
                table: "final_approvals");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "final_approvals");
        }
    }
}
