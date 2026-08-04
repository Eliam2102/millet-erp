using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AplicacionPagos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "propuesta_aplicacion_pago",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deposito_ref = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    monto_deposito = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    remittance_ref = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ajuste_no_fiscal = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    motivo_rechazo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    resuelta_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_propuesta_aplicacion_pago", x => x.id);
                    table.CheckConstraint("ck_propuesta_aplicacion_ajuste_no_positivo", "ajuste_no_fiscal <= 0");
                    table.CheckConstraint("ck_propuesta_aplicacion_estado", "estado IN (1, 2, 3)");
                    table.CheckConstraint("ck_propuesta_aplicacion_monto_positivo", "monto_deposito > 0");
                    table.CheckConstraint("ck_propuesta_aplicacion_rechazo_consistente", "(estado = 3 AND motivo_rechazo IS NOT NULL) OR (estado != 3 AND motivo_rechazo IS NULL)");
                    table.CheckConstraint("ck_propuesta_aplicacion_resolucion_consistente", "(estado = 1 AND resuelta_por IS NULL AND resuelta_en IS NULL) OR (estado != 1 AND resuelta_por IS NOT NULL AND resuelta_en IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "propuesta_aplicacion_factura",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    propuesta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_cartera_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    importe_aplicado = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    num_parcialidad = table.Column<int>(type: "integer", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_propuesta_aplicacion_factura", x => x.id);
                    table.CheckConstraint("ck_propuesta_factura_importe_positivo", "importe_aplicado > 0");
                    table.ForeignKey(
                        name: "fk_propuesta_aplicacion_factura_propuesta_aplicacion_pago_prop",
                        column: x => x.propuesta_id,
                        principalSchema: "cuentas_por_cobrar",
                        principalTable: "propuesta_aplicacion_pago",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_propuesta_factura_uuid",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_factura",
                columns: new[] { "propuesta_id", "factura_uuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_propuesta_aplicacion_cliente_estado",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                columns: new[] { "cliente_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_propuesta_aplicacion_pendientes",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                column: "estado",
                filter: "estado = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "propuesta_aplicacion_factura",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropTable(
                name: "propuesta_aplicacion_pago",
                schema: "cuentas_por_cobrar");
        }
    }
}
