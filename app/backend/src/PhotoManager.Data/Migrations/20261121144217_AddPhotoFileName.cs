using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoFileName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "file_name",
                table: "photos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Every photo keeps the name it has until an AI description renames it (docs/adr/0014); photos described
            // before this change are not renamed.
            migrationBuilder.Sql("UPDATE photos SET file_name = original_file_name;");

            // The previous release keeps serving during a rollout, and comes back on a rollback, without knowing this
            // column: a photo it inserts gets the name it arrived with, as this release would give it, instead of an
            // empty name. This release always sets the name, so the trigger changes nothing for it. It can be dropped
            // once no release older than this one can run against the database.
            migrationBuilder.Sql("ALTER TABLE photos ALTER COLUMN file_name DROP DEFAULT;");
            migrationBuilder.Sql(
                """
                CREATE FUNCTION photos_default_file_name() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.file_name IS NULL THEN
                        NEW.file_name := NEW.original_file_name;
                    END IF;
                    RETURN NEW;
                END
                $$;
                """);
            migrationBuilder.Sql("CREATE TRIGGER photos_default_file_name BEFORE INSERT ON photos FOR EACH ROW EXECUTE FUNCTION photos_default_file_name();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS photos_default_file_name ON photos;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS photos_default_file_name();");
            migrationBuilder.DropColumn(
                name: "file_name",
                table: "photos");
        }
    }
}
