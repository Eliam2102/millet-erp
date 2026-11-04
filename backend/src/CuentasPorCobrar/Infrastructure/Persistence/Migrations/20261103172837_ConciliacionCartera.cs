using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConciliacionCartera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conciliacion_cartera",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    ejecutada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    ejecutada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    comprobantes_revisados = table.Column<int>(type: "integer", nullable: false),
                    total_diferencias = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_cartera", x => x.id);
                    table.CheckConstraint("ck_conciliacion_cartera_conteos", "comprobantes_revisados >= 0 AND total_diferencias >= 0");
                    table.CheckConstraint("ck_conciliacion_cartera_origen", "origen IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "conciliacion_cartera_diferencia",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conciliacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_comprobante = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    uuid = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receptor_rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    receptor_nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    importe_facturacion = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    importe_cartera = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    causa_probable = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_cartera_diferencia", x => x.id);
                    table.CheckConstraint("ck_conciliacion_cartera_diferencia_tipo", "tipo BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "fk_conciliacion_cartera_diferencia_conciliacion_cartera_concil",
                        column: x => x.conciliacion_id,
                        principalSchema: "cuentas_por_cobrar",
                        principalTable: "conciliacion_cartera",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_cartera_empresa_fecha_origen",
                schema: "cuentas_por_cobrar",
                table: "conciliacion_cartera",
                columns: new[] { "empresa_id", "fecha", "origen" });

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_cartera_diferencia_conciliacion",
                schema: "cuentas_por_cobrar",
                table: "conciliacion_cartera_diferencia",
                column: "conciliacion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conciliacion_cartera_diferencia",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropTable(
                name: "conciliacion_cartera",
                schema: "cuentas_por_cobrar");
        }
    }
}
