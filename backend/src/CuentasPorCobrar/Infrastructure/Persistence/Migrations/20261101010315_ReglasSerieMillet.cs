using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// C7.2 (86e3kdxy6): carga las series de Millet (Cuestionario 04, R2) en
    /// regla_liberacion_serie y retira las de ejemplo de la semilla anterior.
    /// Las reglas pasan a ser configuración (sin HasData): no se pisa una
    /// serie que ya exista (p. ej. cargada a mano en una base de demo) y solo
    /// se borran las filas de ejemplo que sigan tal como las dejó la semilla.
    /// </summary>
    public partial class ReglasSerieMillet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM cuentas_por_cobrar.regla_liberacion_serie
                WHERE id IN ('0000000a-1001-0000-0000-000000000003',
                             '0000000a-1001-0000-0000-000000000004',
                             '0000000a-1001-0000-0000-000000000005')
                  AND created_by = 'seed' AND updated_by = 'seed';
                """);

            migrationBuilder.Sql("""
                INSERT INTO cuentas_por_cobrar.regla_liberacion_serie
                    (id, prefijo, comportamiento, activo, version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT v.id::uuid, v.prefijo, v.comportamiento, true, 1, now(), now(), 'seed', 'seed', NULL
                FROM (VALUES
                    ('0000000a-1001-0000-0000-000000000001', '5000', 1::smallint),
                    ('0000000a-1001-0000-0000-000000000006', '5500', 1::smallint),
                    ('0000000a-1001-0000-0000-000000000007', '5600', 1::smallint),
                    ('0000000a-1001-0000-0000-000000000002', '7000', 1::smallint),
                    ('0000000a-1001-0000-0000-000000000008', '7200', 1::smallint),
                    ('0000000a-1001-0000-0000-000000000009', '7500', 1::smallint),
                    ('0000000a-1001-0000-0000-00000000000a', '5800', 2::smallint),
                    ('0000000a-1001-0000-0000-00000000000b', '5900', 2::smallint),
                    ('0000000a-1001-0000-0000-00000000000c', '7800', 2::smallint),
                    ('0000000a-1001-0000-0000-00000000000d', '7900', 2::smallint),
                    ('0000000a-1001-0000-0000-00000000000e', '9000', 2::smallint)
                ) AS v(id, prefijo, comportamiento)
                WHERE NOT EXISTS (
                    SELECT 1 FROM cuentas_por_cobrar.regla_liberacion_serie r
                    WHERE r.id = v.id::uuid OR r.prefijo = v.prefijo);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Solo retira las series nuevas que sigan como las dejó esta
            // migración; 5000/7000 ya existían y las de ejemplo no se reponen.
            migrationBuilder.Sql("""
                DELETE FROM cuentas_por_cobrar.regla_liberacion_serie
                WHERE id IN ('0000000a-1001-0000-0000-000000000006', '0000000a-1001-0000-0000-000000000007',
                             '0000000a-1001-0000-0000-000000000008', '0000000a-1001-0000-0000-000000000009',
                             '0000000a-1001-0000-0000-00000000000a', '0000000a-1001-0000-0000-00000000000b',
                             '0000000a-1001-0000-0000-00000000000c', '0000000a-1001-0000-0000-00000000000d',
                             '0000000a-1001-0000-0000-00000000000e')
                  AND updated_by = 'seed';
                """);
        }
    }
}
