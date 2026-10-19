using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DevolucionesCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "devolucion_cliente",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    movimiento_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota_credito_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota_credito_folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    nota_credito_uuid = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    importe_maximo_origen = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    motivo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    destino_banco = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    destino_clabe = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    destino_titular = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    solicitada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    solicitada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    autorizada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resuelta_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    movimiento_pago_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pagada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    pagada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devolucion_cliente", x => x.id);
                    table.CheckConstraint("ck_devolucion_cliente_estado", "estado IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_devolucion_cliente_importe", "importe > 0");
                    table.CheckConstraint("ck_devolucion_cliente_origen", "(origen = 1 AND movimiento_origen_id IS NOT NULL) OR (origen = 2 AND nota_credito_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_devolucion_cliente_movimiento_bancario_movimiento_origen_id",
                        column: x => x.movimiento_origen_id,
                        principalSchema: "tesoreria",
                        principalTable: "movimiento_bancario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_devolucion_cliente_movimiento_bancario_movimiento_pago_id",
                        column: x => x.movimiento_pago_id,
                        principalSchema: "tesoreria",
                        principalTable: "movimiento_bancario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_devolucion_cliente_empresa_estado",
                schema: "tesoreria",
                table: "devolucion_cliente",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_devolucion_cliente_movimiento_origen",
                schema: "tesoreria",
                table: "devolucion_cliente",
                column: "movimiento_origen_id");

            migrationBuilder.CreateIndex(
                name: "ix_devolucion_cliente_movimiento_pago_id",
                schema: "tesoreria",
                table: "devolucion_cliente",
                column: "movimiento_pago_id");

            migrationBuilder.CreateIndex(
                name: "ux_devolucion_cliente_nota_credito",
                schema: "tesoreria",
                table: "devolucion_cliente",
                column: "nota_credito_id",
                unique: true,
                filter: "nota_credito_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "devolucion_cliente",
                schema: "tesoreria");
        }
    }
}
