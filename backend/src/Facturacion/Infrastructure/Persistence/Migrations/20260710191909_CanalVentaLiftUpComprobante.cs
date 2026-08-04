using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Lift-up de canal de venta a la raíz TPT (`[Decisión 12-D]`, CAJAS-PR2):
    /// la columna <c>canal_venta</c> sube de <c>factura_venta</c> a
    /// <c>comprobante</c> como nullable, con data migration 1:1 ANTES del drop
    /// (el scaffold ordenaba el drop primero — reordenado a mano para no
    /// perder datos). Comprobantes no-factura quedan NULL → "Sin asignar".
    /// </summary>
    public partial class CanalVentaLiftUpComprobante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "canal_venta",
                schema: "facturacion",
                table: "comprobante",
                type: "smallint",
                nullable: true);

            // Data migration 1:1: cada factura_venta (PK = FK a comprobante)
            // copia su canal a la fila base antes de perder la columna.
            migrationBuilder.Sql(
                """
                UPDATE facturacion.comprobante c
                SET canal_venta = fv.canal_venta
                FROM facturacion.factura_venta fv
                WHERE fv.id = c.id;
                """);

            migrationBuilder.DropColumn(
                name: "canal_venta",
                schema: "facturacion",
                table: "factura_venta");

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_sucursal_canal",
                schema: "facturacion",
                table: "comprobante",
                columns: new[] { "sucursal_id", "canal_venta" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_comprobante_sucursal_canal",
                schema: "facturacion",
                table: "comprobante");

            migrationBuilder.AddColumn<short>(
                name: "canal_venta",
                schema: "facturacion",
                table: "factura_venta",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.Sql(
                """
                UPDATE facturacion.factura_venta fv
                SET canal_venta = COALESCE(c.canal_venta, 0)
                FROM facturacion.comprobante c
                WHERE c.id = fv.id;
                """);

            migrationBuilder.DropColumn(
                name: "canal_venta",
                schema: "facturacion",
                table: "comprobante");
        }
    }
}
