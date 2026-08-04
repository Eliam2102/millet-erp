using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CajaSesiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "autorizacion_apertura_caja",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cajero_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supervisor_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    fecha_autorizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vigente_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    caja_sesion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autorizacion_apertura_caja", x => x.id);
                    table.ForeignKey(
                        name: "fk_autorizacion_apertura_caja_caja_caja_id",
                        column: x => x.caja_id,
                        principalSchema: "facturacion",
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "caja_ajuste_pendiente",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cobro_mostrador_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    forma_pago = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    motivo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    aplicado_en_sesion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caja_ajuste_pendiente", x => x.id);
                    table.ForeignKey(
                        name: "fk_caja_ajuste_pendiente_caja_caja_id",
                        column: x => x.caja_id,
                        principalSchema: "facturacion",
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "caja_sesion",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsable_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    dia_operacion = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_apertura = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_cierre = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fondo_apertura = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    autorizacion_apertura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    efectivo_declarado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    efectivo_teorico = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    diferencia = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cierre_extemporaneo = table.Column<bool>(type: "boolean", nullable: false),
                    notas_cierre = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caja_sesion", x => x.id);
                    table.ForeignKey(
                        name: "fk_caja_sesion_caja_caja_id",
                        column: x => x.caja_id,
                        principalSchema: "facturacion",
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "caja_movimiento",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_sesion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    forma_pago = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cobro_mostrador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    descripcion = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caja_movimiento", x => x.id);
                    table.ForeignKey(
                        name: "fk_caja_movimiento_caja_sesion_caja_sesion_id",
                        column: x => x.caja_sesion_id,
                        principalSchema: "facturacion",
                        principalTable: "caja_sesion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "caja_sesion_corte",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_sesion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forma_pago = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    monto_sistema = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    monto_declarado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caja_sesion_corte", x => x.id);
                    table.ForeignKey(
                        name: "fk_caja_sesion_corte_caja_sesiones_caja_sesion_id",
                        column: x => x.caja_sesion_id,
                        principalSchema: "facturacion",
                        principalTable: "caja_sesion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_autorizacion_apertura_lookup",
                schema: "facturacion",
                table: "autorizacion_apertura_caja",
                columns: new[] { "caja_id", "cajero_usuario_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_caja_ajuste_pendiente_caja",
                schema: "facturacion",
                table: "caja_ajuste_pendiente",
                column: "caja_id",
                filter: "aplicado_en_sesion_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_caja_movimiento_cobro",
                schema: "facturacion",
                table: "caja_movimiento",
                column: "cobro_mostrador_id",
                filter: "cobro_mostrador_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_caja_movimiento_sesion",
                schema: "facturacion",
                table: "caja_movimiento",
                column: "caja_sesion_id");

            migrationBuilder.CreateIndex(
                name: "ix_caja_sesion_caja_dia",
                schema: "facturacion",
                table: "caja_sesion",
                columns: new[] { "caja_id", "dia_operacion" });

            migrationBuilder.CreateIndex(
                name: "ix_caja_sesion_responsable_estado",
                schema: "facturacion",
                table: "caja_sesion",
                columns: new[] { "responsable_usuario_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_caja_sesion_no_cerrada",
                schema: "facturacion",
                table: "caja_sesion",
                column: "caja_id",
                unique: true,
                filter: "estado IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_caja_sesion_corte",
                schema: "facturacion",
                table: "caja_sesion_corte",
                columns: new[] { "caja_sesion_id", "forma_pago" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autorizacion_apertura_caja",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "caja_ajuste_pendiente",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "caja_movimiento",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "caja_sesion_corte",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "caja_sesion",
                schema: "facturacion");
        }
    }
}
