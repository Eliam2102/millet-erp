using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConteosInventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conteos_inventario",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    filtro_familia = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    fecha_planificada = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_cierre = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_capturado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aprobador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_aprobacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conteos_inventario", x => x.id);
                    table.CheckConstraint("ck_conteos_estado", "estado BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_conteos_tipo", "tipo BETWEEN 0 AND 1");
                });

            migrationBuilder.CreateTable(
                name: "recuentos_conteo",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_conteo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secuencia = table.Column<int>(type: "integer", nullable: false),
                    cantidad_recontada = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    capturado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    capturado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recuentos_conteo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lineas_conteo",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conteo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad_teorica = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    costo_promedio_snapshot = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    cantidad_real_capturada = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    capturado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    capturado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requiere_recuento = table.Column<bool>(type: "boolean", nullable: false),
                    aprobado_individualmente = table.Column<bool>(type: "boolean", nullable: false),
                    justificacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lineas_conteo", x => x.id);
                    table.ForeignKey(
                        name: "fk_lineas_conteo_conteos_inventario_conteo_id",
                        column: x => x.conteo_id,
                        principalSchema: "almacen",
                        principalTable: "conteos_inventario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conteos_activos",
                schema: "almacen",
                table: "conteos_inventario",
                columns: new[] { "estado", "fecha_planificada" },
                filter: "estado IN (0, 1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "ix_lineas_conteo_conteo_id",
                schema: "almacen",
                table: "lineas_conteo",
                column: "conteo_id");

            migrationBuilder.CreateIndex(
                name: "ux_lineas_conteo",
                schema: "almacen",
                table: "lineas_conteo",
                columns: new[] { "conteo_id", "articulo_id", "sub_almacen_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recuentos_secuencia",
                schema: "almacen",
                table: "recuentos_conteo",
                columns: new[] { "linea_conteo_id", "secuencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lineas_conteo",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "recuentos_conteo",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "conteos_inventario",
                schema: "almacen");
        }
    }
}
