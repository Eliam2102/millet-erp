using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AsignacionArticuloUbicacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asignaciones_articulo_ubicacion",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    minimo = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    maximo = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    punto_reorden = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    auto_requisicion = table.Column<bool>(type: "boolean", nullable: false),
                    objetivo = table.Column<short>(type: "smallint", nullable: false),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignaciones_articulo_ubicacion", x => x.id);
                    table.CheckConstraint("ck_asignaciones_estatus", "estatus BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_asignaciones_max_no_menor_min", "maximo >= minimo");
                    table.CheckConstraint("ck_asignaciones_niveles_no_negativos", "minimo >= 0 AND maximo >= 0 AND punto_reorden >= 0");
                    table.CheckConstraint("ck_asignaciones_objetivo", "objetivo BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_asignaciones_articulo_ubicacion_ubicaciones_ubicacion_id",
                        column: x => x.ubicacion_id,
                        principalSchema: "almacen",
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_articulo_ubicacion_articulo_id",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                column: "articulo_id");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_articulo_ubicacion_estatus",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ux_asignaciones_ubicacion_articulo",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                columns: new[] { "ubicacion_id", "articulo_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignaciones_articulo_ubicacion",
                schema: "almacen");
        }
    }
}
