using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// M-174 (86e3zr6yv) · Contabilidad, 6-nov: una póliza por cada cobro en caja, también en efectivo; el corte no genera asiento.
/// 1) Asigna los roles EFECTIVO_CAJA_COBRANZA y CAJA a 101.01.02.00 (efectivo en caja de cobranza) donde la cuenta existe en el
///    catálogo y el rol sigue sin cuenta (no pisa una asignación hecha a mano). Sin la cuenta, el rol queda como estaba.
/// 2) Ajusta concepto y descripción de la plantilla del efectivo (antes «una póliza por corte»), solo en la versión sembrada.
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261109120000_M174EfectivoPorCobro")]
public sealed class M174EfectivoPorCobro : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            WITH por_asignar AS (
                SELECT r.id AS rol_id, r.empresa_id, c.id AS cuenta_id
                FROM contabilidad.roles_cuenta r
                JOIN contabilidad.cuentas_contables c
                  ON c.empresa_id = r.empresa_id AND c.codigo = '101.01.02.00' AND c.deleted_at IS NULL
                WHERE r.codigo IN ('EFECTIVO_CAJA_COBRANZA', 'CAJA') AND r.deleted_at IS NULL
                  AND r.sucursal_id IS NULL AND r.moneda IS NULL AND r.cuenta_id IS NULL
            ),
            asignados AS (
                UPDATE contabilidad.roles_cuenta r
                   SET cuenta_id = p.cuenta_id, version = r.version + 1,
                       updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-M174'
                  FROM por_asignar p
                 WHERE r.id = p.rol_id
                RETURNING r.id, r.empresa_id, p.cuenta_id
            )
            INSERT INTO contabilidad.roles_cuenta_cambios
                (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
            SELECT gen_random_uuid(), a.empresa_id, a.id, NULL, a.cuenta_id, 'migration-M174', now() AT TIME ZONE 'UTC',
                   'M-174: efectivo en caja de cobranza 101.01.02.00 (Contabilidad, 6-nov)'
            FROM asignados a;

            UPDATE contabilidad.plantillas_contables
               SET concepto = 'Cobro en efectivo',
                   descripcion = 'Plantilla #11b (M-174): un cobro en efectivo, efectivo en caja de cobranza contra el cliente; una póliza por cobro',
                   version = version + 1, updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-M174'
             WHERE tipo_evento = 'facturacion.caja-sesion.cerrada.v1' AND created_by = 'migration-C15' AND deleted_at IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE contabilidad.roles_cuenta r
               SET cuenta_id = NULL, version = r.version + 1, updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-M174-down'
             WHERE r.updated_by = 'migration-M174' AND r.codigo IN ('EFECTIVO_CAJA_COBRANZA', 'CAJA');
            DELETE FROM contabilidad.roles_cuenta_cambios WHERE usuario = 'migration-M174';
            UPDATE contabilidad.plantillas_contables
               SET concepto = 'Liquidación de efectivo',
                   descripcion = 'Plantilla #11b: efectivo en caja de cobranza contra clientes, un abono por cobro (una póliza por corte del cajero)',
                   updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-M174-down'
             WHERE tipo_evento = 'facturacion.caja-sesion.cerrada.v1' AND created_by = 'migration-C15' AND deleted_at IS NULL;
            """);
    }
}
