using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class K102SiembraReglasGrupoC03 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // K10.2 (cuestionario 08, C-03): la regla por omisión es la del grupo de cuentas = rubro del catálogo de Millet.
            // Ingresos (400) → ubicación y cliente; costo (500) → ubicación y área; gastos de operación (600) → ubicación, área y
            // equipo; balance (100–300) sin regla. 700 «Costo de financiamiento» y 800 «Cuentas de orden» quedan sin regla hasta
            // que Laura/Eliam lo definan. Proyecto opcional (V41). Vigencia desde el día de carga (zona de Mérida); solo para las
            // empresas que ya tienen el rubro y sin duplicar si se vuelve a correr.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.reglas_dimension
                    (id, empresa_id, cuenta_id, tipo_documento_id, dimension, requerimiento, vigente_desde, vigente_hasta, es_prueba, nota,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), c.empresa_id, c.id, NULL, r.dimension, 1, (now() AT TIME ZONE 'America/Merida')::date, NULL,
                       false, 'K10.2 · C-03 cuestionario 08: regla del grupo ' || c.nombre, 1,
                       now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-K102', 'migration-K102', NULL
                FROM contabilidad.cuentas_contables c
                JOIN (VALUES
                    ('400.00.00.00', 1), ('400.00.00.00', 5),
                    ('500.00.00.00', 1), ('500.00.00.00', 2),
                    ('600.00.00.00', 1), ('600.00.00.00', 2), ('600.00.00.00', 3)
                ) AS r(codigo, dimension) ON r.codigo = c.codigo
                WHERE c.clase = 1 AND c.deleted_at IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM contabilidad.reglas_dimension x
                      WHERE x.empresa_id = c.empresa_id AND x.cuenta_id = c.id AND x.tipo_documento_id IS NULL
                        AND x.dimension = r.dimension AND x.deleted_at IS NULL);
                """);

            // Excepción acordada con Uziel (21-oct, decisión (a); confirmar con Eliam/Laura): la capitalización y la depreciación de
            // activos fijos llevan ubicación y área pero no equipo. En el rubro de gastos (600), para los tipos AC (código SAP) y AF
            // (el que usa AF-01), el equipo es opcional; la regla del tipo gana sobre la del grupo.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.reglas_dimension
                    (id, empresa_id, cuenta_id, tipo_documento_id, dimension, requerimiento, vigente_desde, vigente_hasta, es_prueba, nota,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), c.empresa_id, c.id, t.id, 3, 2, (now() AT TIME ZONE 'America/Merida')::date, NULL,
                       false, 'K10.2 · activos fijos (' || t.clave || '): equipo opcional en gastos', 1,
                       now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-K102', 'migration-K102', NULL
                FROM contabilidad.cuentas_contables c
                JOIN contabilidad.tipos_documento_contable t
                  ON t.empresa_id = c.empresa_id AND t.clave IN ('AC', 'AF') AND t.deleted_at IS NULL
                WHERE c.codigo = '600.00.00.00' AND c.clase = 1 AND c.deleted_at IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM contabilidad.reglas_dimension x
                      WHERE x.empresa_id = c.empresa_id AND x.cuenta_id = c.id AND x.tipo_documento_id = t.id
                        AND x.dimension = 3 AND x.deleted_at IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.reglas_dimension r
                WHERE r.created_by = 'migration-K102'
                  AND NOT EXISTS (SELECT 1 FROM contabilidad.reglas_dimension_uso u WHERE u.regla_id = r.id);
                """);
        }
    }
}
