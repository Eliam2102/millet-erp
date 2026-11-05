using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P22RecepcionPorPartida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ce_oc_abierta",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.CreateIndex(
                name: "ix_ce_oc",
                schema: "comercio_exterior",
                table: "expedientes",
                columns: new[] { "empresa_id", "oc_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ce_oc",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.CreateIndex(
                name: "ux_ce_oc_abierta",
                schema: "comercio_exterior",
                table: "expedientes",
                columns: new[] { "empresa_id", "oc_id" },
                unique: true,
                filter: "oc_id IS NOT NULL AND estado = 'Abierto'");
        }
    }
}
