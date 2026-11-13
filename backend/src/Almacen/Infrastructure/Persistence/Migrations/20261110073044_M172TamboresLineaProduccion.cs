using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M172TamboresLineaProduccion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lineas_produccion",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    proceso = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lineas_produccion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tambores_linea",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_produccion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kg_presentacion = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    fecha_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fecha_fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    folio_reemplazo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tambores_linea", x => x.id);
                    table.ForeignKey(
                        name: "fk_tambores_linea_lineas_produccion_linea_produccion_id",
                        column: x => x.linea_produccion_id,
                        principalSchema: "almacen",
                        principalTable: "lineas_produccion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lineas_produccion_sucursal_id_clave",
                schema: "almacen",
                table: "lineas_produccion",
                columns: new[] { "sucursal_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tambores_linea_linea_produccion_id_articulo_id_estado",
                schema: "almacen",
                table: "tambores_linea",
                columns: new[] { "linea_produccion_id", "articulo_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_tambores_linea_movimiento_id",
                schema: "almacen",
                table: "tambores_linea",
                column: "movimiento_id");

            // Carga de muestra (AC-030): Conkal · Insulado 1 e Insulado 2. Sin sucursal Conkal no inserta nada.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('compartido.sucursales') IS NOT NULL THEN
                        INSERT INTO almacen.lineas_produccion
                            (id, sucursal_id, clave, nombre, proceso, activa, version, created_at, updated_at, created_by, updated_by)
                        SELECT v.id, s.id, v.clave, v.nombre, 'INSULADO', true, 0, now(), now(), 'migration-M172', 'migration-M172'
                        FROM (SELECT id FROM compartido.sucursales
                              WHERE upper(clave) = 'CONKAL'
                                 OR ',' || upper(coalesce(clave_aw, '')) || ',' LIKE '%,CONKAL,%'
                                 OR upper(nombre) LIKE '%CONKAL%'
                              ORDER BY (upper(clave) = 'CONKAL') DESC, clave
                              LIMIT 1) s
                        CROSS JOIN (VALUES
                            ('0000000a-0172-0000-0000-000000000001'::uuid, 'INS1', 'Insulado 1'),
                            ('0000000a-0172-0000-0000-000000000002'::uuid, 'INS2', 'Insulado 2')) v(id, clave, nombre)
                        ON CONFLICT DO NOTHING;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tambores_linea",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "lineas_produccion",
                schema: "almacen");
        }
    }
}
