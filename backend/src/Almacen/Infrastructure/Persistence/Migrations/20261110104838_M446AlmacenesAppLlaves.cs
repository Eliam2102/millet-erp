using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M-446: llaves de almacén de la app (sucursal + tipo + línea → ubicación) y traslado interno. Crea la tabla, carga las 16
    /// llaves de Conkal (lista de TI del 9-nov; las demás sucursales «no habilitadas» no se crean), amplía el CHECK de tipo de
    /// movimiento (14 TrasladoInterno, 15 EntradaTrasladoInterno) y hace del 15 una entrada en el trigger del ledger.
    /// </summary>
    public partial class M446AlmacenesAppLlaves : Migration
    {
        private const string EntradasAntes = "v_es_entrada := v_tipo IN (0, 3, 5, 9);";
        private const string EntradasDespues = "v_es_entrada := v_tipo IN (0, 3, 5, 9, 15);";

        /// <summary>Cuerpo vigente del trigger (el de <see cref="EntradaAppAlmacenGeneralAw"/>).</summary>
        private static string TriggerVigente()
        {
            var sinBloque = EntradaGeneralNoVidrio.TriggerNoVidrioAUnica.Replace(EntradaAppAlmacenGeneralAw.BloqueAwAUnica, "");
            if (sinBloque == EntradaGeneralNoVidrio.TriggerNoVidrioAUnica || !sinBloque.Contains(EntradasAntes))
                throw new InvalidOperationException("El cuerpo del trigger del ledger no es el esperado.");
            return sinBloque;
        }

        /// <summary>
        /// Llaves de la lista de TI (9-nov): 4 divisiones × (3 líneas de vidrio + herraje sin línea). La división vive solo en la
        /// app; en A+W las cuatro comparten la ubicación de la línea (de ahí el costo promedio).
        /// </summary>
        internal static readonly (string Tipo, string Linea, string Clave, string Nombre, string UbicacionAw)[] Llaves =
            (from d in new[] { ("EXPORTACION", "EXP", "Exportación"), ("OBRAS", "OBR", "Obras"), ("GENERAL", "GEN", "General"),
                               ("IMPORTACION TEMPORAL", "IMT", "Importación temporal") }
             from l in new[] { ("BYSTRONIC", "CRISTAL - BYSTRONIC"), ("HEGLA 1", "CRISTAL - HEGLA 1"), ("HEGLA 2", "CRISTAL - HEGLA 2"),
                               ("", "HERRAJES - BODEGA") }
             select (d.Item1, l.Item1, $"{d.Item2}-{(l.Item1.Length == 0 ? "HERRAJE" : l.Item1)}",
                     $"{d.Item3} · {(l.Item1.Length == 0 ? "herraje" : l.Item1)}", l.Item2)).ToArray();

        /// <summary>
        /// Carga idempotente (correrla dos veces no duplica) de las llaves de una sucursal: almacén <c>APP-{clave}</c>, un
        /// sub-almacén de materiales directos <c>MP-APP</c> (un movimiento es de un solo sub-almacén: así una entrada o un traslado
        /// entre llaves cabe en uno) y una ubicación por llave. Sin la sucursal no inserta nada. También la usan las pruebas.
        /// </summary>
        public static string SembrarSql(string sucursalClave)
        {
            var clave = sucursalClave.Trim().ToUpperInvariant().Replace("'", "");
            var valores = string.Join(",\n", Llaves.Select(k => $"('{k.Tipo}', '{k.Linea}', '{k.Clave}', '{k.Nombre}', '{k.UbicacionAw}')"));
            return $$"""
                DO $$
                DECLARE v_suc uuid; v_alm uuid; v_sub uuid;
                BEGIN
                    IF to_regclass('compartido.sucursales') IS NULL THEN RETURN; END IF;
                    SELECT id INTO v_suc FROM compartido.sucursales
                     WHERE upper(clave) = '{{clave}}' OR ',' || upper(coalesce(clave_aw, '')) || ',' LIKE '%,{{clave}},%'
                     ORDER BY (upper(clave) = '{{clave}}') DESC, clave LIMIT 1;
                    IF v_suc IS NULL THEN RETURN; END IF;

                    INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at, created_by, updated_by)
                    VALUES (gen_random_uuid(), 'APP-{{clave}}', 'Almacenes de la app · {{clave}}', v_suc, 0, 0, now(), now(), 'migration-M446', 'migration-M446')
                    ON CONFLICT DO NOTHING;
                    SELECT id INTO v_alm FROM almacen.almacenes WHERE clave = 'APP-{{clave}}' AND sucursal_id = v_suc;
                    IF v_alm IS NULL THEN RAISE EXCEPTION 'M446: la clave APP-{{clave}} ya es de otra sucursal'; END IF;

                    INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at, created_by, updated_by)
                    VALUES (gen_random_uuid(), v_alm, 'MP-APP', 'Materia prima de la app de Almacenes', 1, 0, 0, now(), now(), 'migration-M446', 'migration-M446')
                    ON CONFLICT DO NOTHING;
                    SELECT id INTO v_sub FROM almacen.sub_almacenes WHERE almacen_id = v_alm AND clave = 'MP-APP';

                    INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at, created_by, updated_by)
                    SELECT gen_random_uuid(), v_sub, k.clave, k.nombre, 0, false, 0, now(), now(), 'migration-M446', 'migration-M446'
                      FROM (VALUES {{valores}}) k(tipo, linea, clave, nombre, aw)
                    ON CONFLICT DO NOTHING;

                    INSERT INTO almacen.almacenes_app (id, sucursal_id, tipo_almacen, linea, ubicacion_id, ubicacion_aw, activo, version, created_at, updated_at, created_by, updated_by)
                    SELECT gen_random_uuid(), v_suc, k.tipo, k.linea, u.id, k.aw, true, 0, now(), now(), 'migration-M446', 'migration-M446'
                      FROM (VALUES {{valores}}) k(tipo, linea, clave, nombre, aw)
                      JOIN almacen.ubicaciones u ON u.sub_almacen_id = v_sub AND u.clave = k.clave
                    ON CONFLICT DO NOTHING;
                END $$;
                """;
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.CreateTable(
                name: "almacenes_app",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_almacen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    linea = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion_aw = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_almacenes_app", x => x.id);
                    table.ForeignKey(
                        name: "fk_almacenes_app_ubicaciones_ubicacion_id",
                        column: x => x.ubicacion_id,
                        principalSchema: "almacen",
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 15");

            migrationBuilder.CreateIndex(
                name: "ix_almacenes_app_sucursal_id_tipo_almacen_linea",
                schema: "almacen",
                table: "almacenes_app",
                columns: new[] { "sucursal_id", "tipo_almacen", "linea" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_almacenes_app_ubicacion_id",
                schema: "almacen",
                table: "almacenes_app",
                column: "ubicacion_id");

            migrationBuilder.Sql(TriggerVigente().Replace(EntradasAntes, EntradasDespues));
            migrationBuilder.Sql(SembrarSql("CONKAL"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Las ubicaciones, el sub-almacén y el almacén APP-* quedan (pueden tener saldo); solo se quita la tabla de llaves.
            migrationBuilder.Sql(TriggerVigente());
            migrationBuilder.DropTable(
                name: "almacenes_app",
                schema: "almacen");

            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 13");
        }
    }
}
