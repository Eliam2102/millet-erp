using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OcEstadoComercialRespuestaRegistrada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_estado_comercial_proveedor",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_estado_comercial_proveedor",
                schema: "compras",
                table: "ordenes_compra",
                sql: "estado_comercial_proveedor BETWEEN 0 AND 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_estado_comercial_proveedor",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_estado_comercial_proveedor",
                schema: "compras",
                table: "ordenes_compra",
                sql: "estado_comercial_proveedor BETWEEN 0 AND 3");
        }
    }
}
