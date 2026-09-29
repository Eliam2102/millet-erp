using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProveedorRfcUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_proveedores_rfc",
                schema: "compartido",
                table: "proveedores");

            // Backfill: normalizar RFC existente antes de exigir unicidad
            // (el dominio ya normaliza en alta/edición desde F1-ADM-05,
            // pero filas previas pudieron quedar con espacios/minúsculas).
            migrationBuilder.Sql("UPDATE compartido.proveedores SET rfc = upper(trim(rfc));");

            migrationBuilder.CreateIndex(
                name: "ux_proveedores_rfc_no_generico",
                schema: "compartido",
                table: "proveedores",
                column: "rfc",
                unique: true,
                filter: "rfc NOT IN ('XAXX010101000','XEXX010101000')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_proveedores_rfc_no_generico",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.CreateIndex(
                name: "ix_proveedores_rfc",
                schema: "compartido",
                table: "proveedores",
                column: "rfc");
        }
    }
}
