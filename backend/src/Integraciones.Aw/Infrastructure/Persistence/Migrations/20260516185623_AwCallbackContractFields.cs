using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AwCallbackContractFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "aw_diagnostic_log",
                schema: "integraciones_aw",
                table: "entidad_externa",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "aw_error_codes",
                schema: "integraciones_aw",
                table: "entidad_externa",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "aw_error_message",
                schema: "integraciones_aw",
                table: "entidad_externa",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aw_diagnostic_log",
                schema: "integraciones_aw",
                table: "entidad_externa");

            migrationBuilder.DropColumn(
                name: "aw_error_codes",
                schema: "integraciones_aw",
                table: "entidad_externa");

            migrationBuilder.DropColumn(
                name: "aw_error_message",
                schema: "integraciones_aw",
                table: "entidad_externa");
        }
    }
}
