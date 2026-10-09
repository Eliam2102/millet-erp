using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CsdVigenciaMetadatos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "csd_not_after",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "csd_not_before",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "csd_not_after",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "csd_not_before",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");
        }
    }
}
