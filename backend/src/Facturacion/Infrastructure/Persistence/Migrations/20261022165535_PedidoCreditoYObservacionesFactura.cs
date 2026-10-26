using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PedidoCreditoYObservacionesFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "condicion_pago_origen",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_credito",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "metodo_pago_origen",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observaciones",
                schema: "facturacion",
                table: "factura_venta",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            // 86e3tfdw2 — backfill de pedidos A+W ya ingeridos: método/condición
            // de pago desde el snapshot MÁS RECIENTE del pedido
            // (pedido_facturable_snapshot.payload_crudo, jsonb serializado por
            // AwSolicitudesSqlReader con JsonSerializerDefaults.Web →
            // cabecera.metodoPago / cabecera.condicionPago). Misma regla que
            // PedidoFacturable.EstablecerPagoOrigen: con condición, crédito =
            // condición ≠ CONTADO; sin condición, crédito = método PPD.
            // Solo origen A+W (origen = 1); Manual y Planta Pintura quedan en null/false.
            migrationBuilder.Sql("""
                UPDATE facturacion.pedido_facturable p
                   SET metodo_pago_origen = s.metodo,
                       condicion_pago_origen = s.condicion,
                       es_credito = CASE
                           WHEN s.condicion IS NOT NULL THEN upper(s.condicion) <> 'CONTADO'
                           ELSE coalesce(s.metodo = 'PPD', false)
                       END
                  FROM (
                        SELECT DISTINCT ON (pedido_facturable_id)
                               pedido_facturable_id,
                               upper(rtrim(left(nullif(btrim(payload_crudo -> 'cabecera' ->> 'metodoPago'), ''), 5))) AS metodo,
                               rtrim(left(nullif(btrim(payload_crudo -> 'cabecera' ->> 'condicionPago'), ''), 50)) AS condicion
                          FROM facturacion.pedido_facturable_snapshot
                         ORDER BY pedido_facturable_id, leido_at DESC, id DESC
                       ) s
                 WHERE p.id = s.pedido_facturable_id
                   AND p.origen = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "condicion_pago_origen",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropColumn(
                name: "es_credito",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropColumn(
                name: "metodo_pago_origen",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropColumn(
                name: "observaciones",
                schema: "facturacion",
                table: "factura_venta");
        }
    }
}
