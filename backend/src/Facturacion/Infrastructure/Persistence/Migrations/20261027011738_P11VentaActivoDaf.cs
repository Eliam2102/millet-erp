using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P11VentaActivoDaf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "evidencia",
                schema: "facturacion",
                table: "autorizacion_venta_activo",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<short>(
                name: "motivo",
                schema: "facturacion",
                table: "autorizacion_venta_activo",
                type: "smallint",
                nullable: false,
                defaultValue: (short)5);

            migrationBuilder.AddColumn<string>(
                name: "solicitado_por",
                schema: "facturacion",
                table: "autorizacion_venta_activo",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "Sin dato (anterior al 26-oct-2026)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "evidencia",
                schema: "facturacion",
                table: "autorizacion_venta_activo");

            migrationBuilder.DropColumn(
                name: "motivo",
                schema: "facturacion",
                table: "autorizacion_venta_activo");

            migrationBuilder.DropColumn(
                name: "solicitado_por",
                schema: "facturacion",
                table: "autorizacion_venta_activo");
        }
    }
}
