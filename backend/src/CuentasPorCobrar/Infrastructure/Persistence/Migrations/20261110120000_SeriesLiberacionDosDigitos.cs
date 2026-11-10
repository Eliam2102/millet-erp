using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// R1 (Crédito y Cobranza, 9-nov): la serie y subserie del pedido son sus 2 primeros dígitos, no 4. Las reglas sembradas por
    /// ReglasSerieMillet usaban «5800», «9000»…, y un pedido como 90047568 no casaba con «9000» y caía en «evalúa crédito».
    /// Pasa cada regla sembrada a su prefijo de 2 dígitos solo si sigue con el prefijo original y no existe ya una regla con el
    /// nuevo; las reglas capturadas a mano no se tocan.
    /// </summary>
    [DbContext(typeof(CuentasPorCobrarDbContext))]
    [Migration("20261110120000_SeriesLiberacionDosDigitos")]
    public partial class SeriesLiberacionDosDigitos : Migration
    {
        private const string Series = """
                    ('0000000a-1001-0000-0000-000000000001', '5000', '50'),
                    ('0000000a-1001-0000-0000-000000000006', '5500', '55'),
                    ('0000000a-1001-0000-0000-000000000007', '5600', '56'),
                    ('0000000a-1001-0000-0000-000000000002', '7000', '70'),
                    ('0000000a-1001-0000-0000-000000000008', '7200', '72'),
                    ('0000000a-1001-0000-0000-000000000009', '7500', '75'),
                    ('0000000a-1001-0000-0000-00000000000a', '5800', '58'),
                    ('0000000a-1001-0000-0000-00000000000b', '5900', '59'),
                    ('0000000a-1001-0000-0000-00000000000c', '7800', '78'),
                    ('0000000a-1001-0000-0000-00000000000d', '7900', '79'),
                    ('0000000a-1001-0000-0000-00000000000e', '9000', '90')
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE cuentas_por_cobrar.regla_liberacion_serie r
                   SET prefijo = v.nuevo, version = r.version + 1, updated_at = now(), updated_by = 'migration-R1'
                  FROM (VALUES
                {Series}
                  ) AS v(id, anterior, nuevo)
                 WHERE r.id = v.id::uuid AND r.prefijo = v.anterior
                   AND NOT EXISTS (SELECT 1 FROM cuentas_por_cobrar.regla_liberacion_serie x WHERE x.prefijo = v.nuevo);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE cuentas_por_cobrar.regla_liberacion_serie r
                   SET prefijo = v.anterior, version = r.version + 1, updated_at = now(), updated_by = 'migration-R1-down'
                  FROM (VALUES
                {Series}
                  ) AS v(id, anterior, nuevo)
                 WHERE r.id = v.id::uuid AND r.prefijo = v.nuevo AND r.updated_by = 'migration-R1'
                   AND NOT EXISTS (SELECT 1 FROM cuentas_por_cobrar.regla_liberacion_serie x WHERE x.prefijo = v.anterior);
                """);
        }
    }
}
