using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A46ConciliacionDiaria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.CreateTable(
                name: "conciliaciones_diarias",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    fuente = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    lineas_comparadas = table.Column<int>(type: "integer", nullable: false),
                    diferencias = table.Column<int>(type: "integer", nullable: false),
                    ejecutada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliaciones_diarias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "diferencias_conciliacion",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conciliacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_aw_variante_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lote = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    area_hoja_m2 = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: true),
                    teorico = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    fisico = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    sentido = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    costo_unitario_mxn = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    valor_mxn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    folio_movimiento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    estado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    motivo_aclaracion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    aclarada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    aclarada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diferencias_conciliacion", x => x.id);
                    table.CheckConstraint("ck_diferencias_conciliacion_distintas", "teorico <> fisico");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 12");

            migrationBuilder.CreateIndex(
                name: "ix_conciliaciones_diarias_empresa_id_almacen_id_fecha",
                schema: "almacen",
                table: "conciliaciones_diarias",
                columns: new[] { "empresa_id", "almacen_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_diferencias_conciliacion_conciliacion_id",
                schema: "almacen",
                table: "diferencias_conciliacion",
                column: "conciliacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_diferencias_conciliacion_empresa_id_estado_fecha",
                schema: "almacen",
                table: "diferencias_conciliacion",
                columns: new[] { "empresa_id", "estado", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conciliaciones_diarias",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "diferencias_conciliacion",
                schema: "almacen");

            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 11");
        }
    }
}
