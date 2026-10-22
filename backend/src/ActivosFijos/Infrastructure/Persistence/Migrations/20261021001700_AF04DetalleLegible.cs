using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AF04DetalleLegible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "centro_costo",
                schema: "activos_fijos",
                table: "depreciaciones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "clase",
                schema: "activos_fijos",
                table: "depreciaciones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "equipo",
                schema: "activos_fijos",
                table: "depreciaciones",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sucursal",
                schema: "activos_fijos",
                table: "depreciaciones",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "centro_costo",
                schema: "activos_fijos",
                table: "depreciaciones");

            migrationBuilder.DropColumn(
                name: "clase",
                schema: "activos_fijos",
                table: "depreciaciones");

            migrationBuilder.DropColumn(
                name: "equipo",
                schema: "activos_fijos",
                table: "depreciaciones");

            migrationBuilder.DropColumn(
                name: "sucursal",
                schema: "activos_fijos",
                table: "depreciaciones");
        }
    }
}
