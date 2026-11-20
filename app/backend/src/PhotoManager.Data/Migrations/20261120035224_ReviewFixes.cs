using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_photos_status_taken_at_id",
                table: "photos");

            migrationBuilder.AddColumn<long>(
                name: "requested_by_id",
                table: "prepared_downloads",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "skipped_count",
                table: "prepared_downloads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "uploaded_by_id",
                table: "photos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "run_id",
                table: "maintenance_jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "actor_id",
                table: "audit_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sort_date",
                table: "photos",
                type: "timestamp with time zone",
                nullable: false,
                computedColumnSql: "COALESCE(taken_at, uploaded_at)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_photos_status_sort_date_id",
                table: "photos",
                columns: new[] { "status", "sort_date", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_photos_uploaded_by_id_uploaded_at",
                table: "photos",
                columns: new[] { "uploaded_by_id", "uploaded_at" });

            // Before the unique index can exist, existing byte-identical "Ready" copies must be resolved: the
            // earliest copy stays in the library and the others become Duplicate (nothing is deleted).
            migrationBuilder.Sql(
                """
                UPDATE photos AS p
                SET status = 'Duplicate', status_detail = 'Identical to another photo in the library.', updated_at = now()
                FROM (
                    SELECT id, row_number() OVER (PARTITION BY sha256 ORDER BY uploaded_at, id) AS copy_number
                    FROM photos
                    WHERE status = 'Ready' AND sha256 IS NOT NULL
                ) AS ranked
                WHERE p.id = ranked.id AND ranked.copy_number > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_photos_sha256_ready",
                table: "photos",
                column: "sha256",
                unique: true,
                filter: "status = 'Ready' AND sha256 IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_photos_status_sort_date_id",
                table: "photos");

            migrationBuilder.DropIndex(
                name: "ix_photos_uploaded_by_id_uploaded_at",
                table: "photos");

            migrationBuilder.DropIndex(
                name: "ux_photos_sha256_ready",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "sort_date",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "requested_by_id",
                table: "prepared_downloads");

            migrationBuilder.DropColumn(
                name: "skipped_count",
                table: "prepared_downloads");

            migrationBuilder.DropColumn(
                name: "uploaded_by_id",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "run_id",
                table: "maintenance_jobs");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "audit_events");

            migrationBuilder.CreateIndex(
                name: "ix_photos_status_taken_at_id",
                table: "photos",
                columns: new[] { "status", "taken_at", "id" },
                descending: new[] { false, true, true });
        }
    }
}
