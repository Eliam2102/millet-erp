using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProductoAwDatosAduana : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fraccion_arancelaria",
                schema: "compartido",
                table: "producto_aw",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "peso_unitario_kg",
                schema: "compartido",
                table: "producto_aw",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unidad_aduana",
                schema: "compartido",
                table: "producto_aw",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_producto_aw_fraccion",
                schema: "compartido",
                table: "producto_aw",
                sql: "fraccion_arancelaria IS NULL OR fraccion_arancelaria ~ '^[0-9]{8,10}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_producto_aw_peso",
                schema: "compartido",
                table: "producto_aw",
                sql: "peso_unitario_kg IS NULL OR peso_unitario_kg >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_producto_aw_fraccion",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropCheckConstraint(
                name: "ck_producto_aw_peso",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "fraccion_arancelaria",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "peso_unitario_kg",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "unidad_aduana",
                schema: "compartido",
                table: "producto_aw");
        }
    }
}
