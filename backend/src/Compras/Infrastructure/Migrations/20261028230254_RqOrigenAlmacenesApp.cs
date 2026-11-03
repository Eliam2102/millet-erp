using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RqOrigenAlmacenesApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requisiciones_origen",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.AddColumn<string>(
                name: "folio_externo",
                schema: "compras",
                table: "requisiciones",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "alto_mm",
                schema: "compras",
                table: "requisicion_lineas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ancho_mm",
                schema: "compras",
                table: "requisicion_lineas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "piezas",
                schema: "compras",
                table: "requisicion_lineas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisiciones_empresa_id_origen_folio_externo",
                schema: "compras",
                table: "requisiciones",
                columns: new[] { "empresa_id", "origen", "folio_externo" },
                unique: true,
                filter: "folio_externo IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requisiciones_origen",
                schema: "compras",
                table: "requisiciones",
                sql: "origen BETWEEN 0 AND 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requisiciones_empresa_id_origen_folio_externo",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropCheckConstraint(
                name: "ck_requisiciones_origen",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropColumn(
                name: "folio_externo",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropColumn(
                name: "alto_mm",
                schema: "compras",
                table: "requisicion_lineas");

            migrationBuilder.DropColumn(
                name: "ancho_mm",
                schema: "compras",
                table: "requisicion_lineas");

            migrationBuilder.DropColumn(
                name: "piezas",
                schema: "compras",
                table: "requisicion_lineas");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requisiciones_origen",
                schema: "compras",
                table: "requisiciones",
                sql: "origen BETWEEN 0 AND 1");
        }
    }
}
