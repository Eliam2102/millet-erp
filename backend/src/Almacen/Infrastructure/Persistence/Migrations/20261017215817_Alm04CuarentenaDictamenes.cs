using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Alm04CuarentenaDictamenes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.CreateTable(
                name: "dictamenes_revision",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resultado = table.Column<short>(type: "smallint", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    dictaminado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    dictaminado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    movimiento_salida_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movimiento_entrada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    devolucion_a_proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dictamenes_revision", x => x.id);
                    table.CheckConstraint("ck_dictamenes_revision_cantidad", "cantidad > 0");
                    table.CheckConstraint("ck_dictamenes_revision_resultado", "resultado BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_dictamenes_revision_lineas_movimiento_linea_revision_id",
                        column: x => x.linea_revision_id,
                        principalSchema: "almacen",
                        principalTable: "lineas_movimiento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 10");

            migrationBuilder.CreateIndex(
                name: "ix_dictamenes_revision_linea",
                schema: "almacen",
                table: "dictamenes_revision",
                column: "linea_revision_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dictamenes_revision",
                schema: "almacen");

            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 9");
        }
    }
}
