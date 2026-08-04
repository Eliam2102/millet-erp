using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilenameToEnvio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "filename",
                schema: "integraciones_aw",
                table: "envio",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "filename",
                schema: "integraciones_aw",
                table: "envio");
        }
    }
}
