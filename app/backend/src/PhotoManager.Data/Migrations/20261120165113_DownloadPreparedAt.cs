using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class DownloadPreparedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "prepared_at",
                table: "prepared_downloads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_prepared_downloads_prepared_at",
                table: "prepared_downloads",
                column: "prepared_at",
                filter: "prepared_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_prepared_downloads_prepared_at",
                table: "prepared_downloads");

            migrationBuilder.DropColumn(
                name: "prepared_at",
                table: "prepared_downloads");
        }
    }
}
