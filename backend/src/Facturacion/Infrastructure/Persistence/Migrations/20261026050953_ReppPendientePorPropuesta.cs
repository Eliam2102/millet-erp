using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReppPendientePorPropuesta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_repp_pendiente_movimiento_bancario_id",
                schema: "facturacion",
                table: "repp_pendiente");

            migrationBuilder.CreateIndex(
                name: "ux_repp_pendiente_movimiento_propuesta",
                schema: "facturacion",
                table: "repp_pendiente",
                columns: new[] { "movimiento_bancario_id", "propuesta_id" },
                unique: true,
                filter: "propuesta_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_repp_pendiente_movimiento_sin_propuesta",
                schema: "facturacion",
                table: "repp_pendiente",
                column: "movimiento_bancario_id",
                unique: true,
                filter: "propuesta_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_repp_pendiente_movimiento_propuesta",
                schema: "facturacion",
                table: "repp_pendiente");

            migrationBuilder.DropIndex(
                name: "ux_repp_pendiente_movimiento_sin_propuesta",
                schema: "facturacion",
                table: "repp_pendiente");

            migrationBuilder.CreateIndex(
                name: "ix_repp_pendiente_movimiento_bancario_id",
                schema: "facturacion",
                table: "repp_pendiente",
                column: "movimiento_bancario_id",
                unique: true);
        }
    }
}
