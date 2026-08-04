using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventosProcesadosYRecepcionesOcLocal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eventos_procesados",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_tipo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detalle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_procesados", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recepciones_oc_local",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recepcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio_recepcion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_movimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    factura_pendiente = table.Column<bool>(type: "boolean", nullable: false),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observaciones = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    lineas_json = table.Column<string>(type: "jsonb", nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    proyectado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recepciones_oc_local", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_eventos_procesados_id_tipo",
                schema: "cuentas_por_pagar",
                table: "eventos_procesados",
                columns: new[] { "evento_id", "evento_tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recepciones_oc_local_oc",
                schema: "cuentas_por_pagar",
                table: "recepciones_oc_local",
                column: "orden_compra_id");

            migrationBuilder.CreateIndex(
                name: "ix_recepciones_oc_local_pendientes",
                schema: "cuentas_por_pagar",
                table: "recepciones_oc_local",
                columns: new[] { "orden_compra_id", "factura_pendiente" },
                filter: "factura_pendiente = true");

            migrationBuilder.CreateIndex(
                name: "ux_recepciones_oc_local_recepcion",
                schema: "cuentas_por_pagar",
                table: "recepciones_oc_local",
                column: "recepcion_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eventos_procesados",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "recepciones_oc_local",
                schema: "cuentas_por_pagar");
        }
    }
}
