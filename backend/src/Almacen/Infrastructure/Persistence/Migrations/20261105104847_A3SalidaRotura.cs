using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A3SalidaRotura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.CreateTable(
                name: "salidas_rotura",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    unidad_medida = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    motivo_codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    comentario = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    capturado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decidido_por = table.Column<Guid>(type: "uuid", nullable: true),
                    decidido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    folio_movimiento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    costo_unitario_mxn = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    valor_mxn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salidas_rotura", x => x.id);
                    table.CheckConstraint("ck_salidas_rotura_cantidad", "cantidad > 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 13");

            migrationBuilder.CreateIndex(
                name: "ix_salidas_rotura_empresa_id_estado_fecha",
                schema: "almacen",
                table: "salidas_rotura",
                columns: new[] { "empresa_id", "estado", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "salidas_rotura",
                schema: "almacen");

            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 12");
        }
    }
}
