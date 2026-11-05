using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CxpRevaluacionCierre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "revaluaciones_cierre",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    fecha_cierre = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_reversa = table.Column<DateOnly>(type: "date", nullable: false),
                    total_perdida = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_utilidad = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reversa_publicada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revaluaciones_cierre", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "revaluaciones_cierre_detalle",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revaluacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    saldo = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tc_factura = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    tc_cierre = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    importe_libros = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    importe_cierre = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    diferencia = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revaluaciones_cierre_detalle", x => x.id);
                    table.ForeignKey(
                        name: "fk_revaluaciones_cierre_detalle_revaluaciones_cierre_revaluaci",
                        column: x => x.revaluacion_id,
                        principalSchema: "cuentas_por_pagar",
                        principalTable: "revaluaciones_cierre",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_revaluaciones_cierre_reversa_pendiente",
                schema: "cuentas_por_pagar",
                table: "revaluaciones_cierre",
                column: "fecha_reversa",
                filter: "reversa_publicada_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_revaluaciones_cierre_empresa_periodo",
                schema: "cuentas_por_pagar",
                table: "revaluaciones_cierre",
                columns: new[] { "empresa_id", "anio", "mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_revaluaciones_cierre_detalle_proveedor_id",
                schema: "cuentas_por_pagar",
                table: "revaluaciones_cierre_detalle",
                column: "proveedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_revaluaciones_cierre_detalle_revaluacion_id",
                schema: "cuentas_por_pagar",
                table: "revaluaciones_cierre_detalle",
                column: "revaluacion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revaluaciones_cierre_detalle",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "revaluaciones_cierre",
                schema: "cuentas_por_pagar");
        }
    }
}
