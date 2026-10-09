using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P7ApartadosConversionReorden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_capturada",
                schema: "almacen",
                table: "lineas_movimiento",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unidad_capturada",
                schema: "almacen",
                table: "lineas_movimiento",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_fija",
                schema: "almacen",
                table: "configuraciones_reorden",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "apartados_requisicion",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisicion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_requisicion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pendiente = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apartados_requisicion", x => x.id);
                    table.CheckConstraint("ck_apartado_pendiente", "pendiente >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_apartados_requisicion_linea_requisicion_id",
                schema: "almacen",
                table: "apartados_requisicion",
                column: "linea_requisicion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_apartados_requisicion_sucursal_id_articulo_id",
                schema: "almacen",
                table: "apartados_requisicion",
                columns: new[] { "sucursal_id", "articulo_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apartados_requisicion",
                schema: "almacen");

            migrationBuilder.DropColumn(
                name: "cantidad_capturada",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropColumn(
                name: "unidad_capturada",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropColumn(
                name: "cantidad_fija",
                schema: "almacen",
                table: "configuraciones_reorden");
        }
    }
}
