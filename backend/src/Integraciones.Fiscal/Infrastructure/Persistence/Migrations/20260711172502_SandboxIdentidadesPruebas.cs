using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SandboxIdentidadesPruebas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sandbox_emisor_codigo_postal",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sandbox_emisor_razon_social",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sandbox_emisor_regimen_fiscal",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sandbox_emisor_rfc",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sandbox_receptor_codigo_postal",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sandbox_receptor_razon_social",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sandbox_receptor_regimen_fiscal",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sandbox_receptor_rfc",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sandbox_emisor_codigo_postal",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "sandbox_emisor_razon_social",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "sandbox_emisor_regimen_fiscal",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "sandbox_emisor_rfc",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "sandbox_receptor_codigo_postal",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "sandbox_receptor_razon_social",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "sandbox_receptor_regimen_fiscal",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "sandbox_receptor_rfc",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");
        }
    }
}
