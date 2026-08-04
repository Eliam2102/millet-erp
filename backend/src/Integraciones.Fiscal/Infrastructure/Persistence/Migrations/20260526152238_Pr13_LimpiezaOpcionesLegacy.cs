using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pr13_LimpiezaOpcionesLegacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "circuit_breaker_duracion_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "circuit_breaker_failures",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "descarga_backfill_horas",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "descarga_intervalo_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "refresh_batch_size",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "refresh_intervalo_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "retry_count",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "timeout_consulta_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "timeout_descarga_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "circuit_breaker_duracion_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "circuit_breaker_failures",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "descarga_backfill_horas",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "descarga_intervalo_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "refresh_batch_size",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "refresh_intervalo_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "retry_count",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "timeout_consulta_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "timeout_descarga_segundos",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
