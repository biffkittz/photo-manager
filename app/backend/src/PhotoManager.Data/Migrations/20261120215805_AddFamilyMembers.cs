using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PhotoManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "family_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'-1', '-1', '-9223372036854775808', '-1', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    google_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    google_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    invite_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    invite_token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    invite_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invited_by_id = table.Column<long>(type: "bigint", nullable: false),
                    invited_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_sign_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_members", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_family_members_google_subject",
                table: "family_members",
                column: "google_subject",
                unique: true,
                filter: "google_subject IS NOT NULL AND removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_family_members_invite_token_hash",
                table: "family_members",
                column: "invite_token_hash",
                unique: true,
                filter: "invite_token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_members");
        }
    }
}
