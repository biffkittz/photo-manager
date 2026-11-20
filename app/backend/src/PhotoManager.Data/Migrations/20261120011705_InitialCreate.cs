using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace PhotoManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    workflow_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processed_count = table.Column<int>(type: "integer", nullable: false),
                    succeeded_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    encryption_format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    archive_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    archive_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    archive_e_tag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    original_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    description_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    search_keywords = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    description_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rendition_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rendition_version = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    upload_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false)
                        .Annotation("Npgsql:TsVectorConfig", "english")
                        .Annotation("Npgsql:TsVectorProperties", new[] { "description", "original_file_name", "search_keywords" }),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prepared_downloads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    photo_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    blob_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prepared_downloads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_occurred_at",
                table: "audit_events",
                columns: new[] { "actor", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at",
                table: "audit_events",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_jobs_created_at",
                table: "maintenance_jobs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_photos_archive_key",
                table: "photos",
                column: "archive_key",
                unique: true,
                filter: "archive_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_photos_description_state_id",
                table: "photos",
                columns: new[] { "description_state", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_photos_rendition_state_id",
                table: "photos",
                columns: new[] { "rendition_state", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_photos_search_vector",
                table: "photos",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_photos_sha256",
                table: "photos",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_photos_status_last_seen_at",
                table: "photos",
                columns: new[] { "status", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ix_photos_status_taken_at_id",
                table: "photos",
                columns: new[] { "status", "taken_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_photos_status_uploaded_at_id",
                table: "photos",
                columns: new[] { "status", "uploaded_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_photos_upload_batch_id",
                table: "photos",
                column: "upload_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_prepared_downloads_requested_by_created_at",
                table: "prepared_downloads",
                columns: new[] { "requested_by", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "maintenance_jobs");

            migrationBuilder.DropTable(
                name: "photos");

            migrationBuilder.DropTable(
                name: "prepared_downloads");
        }
    }
}
