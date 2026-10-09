using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UmbralesConteoSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "umbral_nivel1maximo",
                schema: "almacen",
                table: "conteos_inventario",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "umbral_nivel2maximo",
                schema: "almacen",
                table: "conteos_inventario",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "variacion_pct_para_recuento",
                schema: "almacen",
                table: "conteos_inventario",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "variacion_valor_para_recuento",
                schema: "almacen",
                table: "conteos_inventario",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
            // Los conteos existentes conservan la política que regía antes de A4.5,
            // aunque los parámetros globales ya hayan cambiado al migrar Administración.
            migrationBuilder.Sql("""
                UPDATE almacen.conteos_inventario
                SET variacion_pct_para_recuento = 5,
                    variacion_valor_para_recuento = 1000,
                    umbral_nivel1maximo = 1000,
                    umbral_nivel2maximo = 10000;
                ALTER TABLE almacen.conteos_inventario
                    ALTER COLUMN variacion_pct_para_recuento DROP DEFAULT,
                    ALTER COLUMN variacion_valor_para_recuento DROP DEFAULT,
                    ALTER COLUMN umbral_nivel1maximo DROP DEFAULT,
                    ALTER COLUMN umbral_nivel2maximo DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "umbral_nivel1maximo",
                schema: "almacen",
                table: "conteos_inventario");

            migrationBuilder.DropColumn(
                name: "umbral_nivel2maximo",
                schema: "almacen",
                table: "conteos_inventario");

            migrationBuilder.DropColumn(
                name: "variacion_pct_para_recuento",
                schema: "almacen",
                table: "conteos_inventario");

            migrationBuilder.DropColumn(
                name: "variacion_valor_para_recuento",
                schema: "almacen",
                table: "conteos_inventario");
        }
    }
}
