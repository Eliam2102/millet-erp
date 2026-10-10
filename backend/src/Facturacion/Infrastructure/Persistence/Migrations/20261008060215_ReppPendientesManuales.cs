using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReppPendientesManuales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repp_pendiente",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_bancario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_bancaria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    propuesta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    monto = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fecha_valor = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_limite = table.Column<DateOnly>(type: "date", nullable: false),
                    referencia = table.Column<string>(type: "text", nullable: true),
                    forma_pago = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    revisado = table.Column<bool>(type: "boolean", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recibo_pago_id = table.Column<Guid>(type: "uuid", nullable: true),
                    intento_recibo_pago_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ultimo_error_codigo = table.Column<string>(type: "text", nullable: true),
                    ultimo_error_mensaje = table.Column<string>(type: "text", nullable: true),
                    motivo_descarte = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repp_pendiente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "repp_pendiente_factura",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repp_pendiente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repp_pendiente_factura", x => x.id);
                    table.ForeignKey(
                        name: "fk_repp_pendiente_factura_repp_pendientes_repp_pendiente_id",
                        column: x => x.repp_pendiente_id,
                        principalSchema: "facturacion",
                        principalTable: "repp_pendiente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_repp_pendiente_empresa_id_estado_fecha_limite",
                schema: "facturacion",
                table: "repp_pendiente",
                columns: new[] { "empresa_id", "estado", "fecha_limite" });

            migrationBuilder.CreateIndex(
                name: "ix_repp_pendiente_intento_recibo_pago_id",
                schema: "facturacion",
                table: "repp_pendiente",
                column: "intento_recibo_pago_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repp_pendiente_movimiento_bancario_id",
                schema: "facturacion",
                table: "repp_pendiente",
                column: "movimiento_bancario_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repp_pendiente_factura_repp_pendiente_id",
                schema: "facturacion",
                table: "repp_pendiente_factura",
                column: "repp_pendiente_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repp_pendiente_factura",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "repp_pendiente",
                schema: "facturacion");
        }
    }
}
