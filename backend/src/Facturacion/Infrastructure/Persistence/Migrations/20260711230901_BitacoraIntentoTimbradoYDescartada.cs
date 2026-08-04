using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BitacoraIntentoTimbradoYDescartada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bitacora_intento_timbrado",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intento_numero = table.Column<int>(type: "integer", nullable: false),
                    resultado = table.Column<short>(type: "smallint", nullable: false),
                    error_codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    error_mensaje = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    registrado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bitacora_intento_timbrado", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_intento_timbrado_comprobante",
                schema: "facturacion",
                table: "bitacora_intento_timbrado",
                columns: new[] { "comprobante_id", "intento_numero" });

            // ── [Decisión 01-G] Migración de datos históricos ─────────────
            // Antes de G5, un timbre fallido dejaba el pedido Importado y el
            // reintento emitía una factura NUEVA: quedaron fallidas "hermanas"
            // muertas. Estados: comprobante 5=TimbradoFallido, 8=Descartada;
            // pedido 1=Importado, 3=Facturado, 4=Cancelado.

            // 1. Fallidas cuyo pedido ya fue tomado por OTRO comprobante o
            //    está cancelado en origen → Descartada (folio quemado, audit
            //    trail intacto).
            migrationBuilder.Sql("""
                UPDATE facturacion.comprobante c
                SET estado = 8, updated_at = now(), version = c.version + 1
                FROM facturacion.factura_venta fv
                JOIN facturacion.pedido_facturable p ON p.id = fv.pedido_facturable_id
                WHERE fv.id = c.id
                  AND c.estado = 5
                  AND c.deleted_at IS NULL
                  AND (
                        (p.estado = 3 AND p.comprobante_vigente_id IS DISTINCT FROM fv.id)
                     OR p.estado = 4
                  );
                """);

            // 2. Pedido aún Importado con VARIAS fallidas vivas → se conserva
            //    la más reciente y el resto se descarta.
            migrationBuilder.Sql("""
                UPDATE facturacion.comprobante c
                SET estado = 8, updated_at = now(), version = c.version + 1
                FROM facturacion.factura_venta fv
                JOIN facturacion.pedido_facturable p ON p.id = fv.pedido_facturable_id
                WHERE fv.id = c.id
                  AND c.estado = 5
                  AND c.deleted_at IS NULL
                  AND p.estado = 1
                  AND c.id NOT IN (
                      SELECT DISTINCT ON (fv2.pedido_facturable_id) c2.id
                      FROM facturacion.factura_venta fv2
                      JOIN facturacion.comprobante c2 ON c2.id = fv2.id
                      WHERE c2.estado = 5 AND c2.deleted_at IS NULL
                        AND fv2.pedido_facturable_id IS NOT NULL
                      ORDER BY fv2.pedido_facturable_id, c2.created_at DESC
                  );
                """);

            // 3. Backfill G5: pedido Importado con exactamente una fallida
            //    viva (tras el paso 2) → queda tomado por ella; el reintento
            //    o el descarte se resuelven sobre esa factura.
            migrationBuilder.Sql("""
                UPDATE facturacion.pedido_facturable p
                SET estado = 3, comprobante_vigente_id = c.id, updated_at = now(), version = p.version + 1
                FROM facturacion.factura_venta fv
                JOIN facturacion.comprobante c ON c.id = fv.id
                WHERE fv.pedido_facturable_id = p.id
                  AND p.estado = 1
                  AND c.estado = 5
                  AND c.deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bitacora_intento_timbrado",
                schema: "facturacion");
        }
    }
}
