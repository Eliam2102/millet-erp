using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RelajarCheckImportacionOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_import_campos",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_import_campos",
                schema: "compras",
                table: "ordenes_compra",
                sql: "(es_importacion = false) OR (estado IN (0, 6)) OR (info_import_incoterm_id IS NOT NULL AND info_import_pais_origen IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_import_campos",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_import_campos",
                schema: "compras",
                table: "ordenes_compra",
                sql: "(es_importacion = false) OR (es_importacion = true AND info_import_incoterm_id IS NOT NULL AND info_import_pais_origen IS NOT NULL AND info_import_numero_contenedor IS NOT NULL)");
        }
    }
}
