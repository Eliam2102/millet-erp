using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// CON-11 (86e3kdypd): asigna a los roles de diferencia cambiaria las cuentas del catálogo de Contabilidad del 5-nov (rubro 701.01 pérdida
/// cambiaria y 702.01 utilidad cambiaria, con subcuenta por origen: .01 clientes, .02 proveedores, .03 bancos).
/// <list type="bullet">
///   <item>Realizada (pagos y cobros): proveedores 701.01.01.02 / 702.01.01.02; clientes 701.01.01.01 / 702.01.01.01.</item>
///   <item>No realizada (revaluación de cierre): bancos 701.01.02.03 / 702.01.02.03; clientes 701.01.02.01 / 702.01.02.01; el rol base de
///   CXP-12 (proveedores) 701.01.02.02 / 702.01.02.02.</item>
/// </list>
/// Mismas reglas que <c>backend/scripts/contabilidad-asignar-cuentas-roles.sql</c> (#485): solo el rol base (sin sucursal ni moneda),
/// solo si está sin cuenta o con una de prueba (990.* o DEMO*), nunca pisa una cuenta real; la cuenta debe existir, estar activa y ser
/// afectable (el catálogo se importa, no viene en migraciones: donde no está cargado, el rol queda sin cuenta y se puede volver a
/// asignar a mano). Cada asignación deja su renglón en la bitácora del rol. Solo datos.
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261107160000_Con11CuentasCambiarias")]
public sealed class Con11CuentasCambiarias : Migration
{
    private const string Usuario = "migration-CON11-cuentas";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        CREATE TEMP TABLE con11_cuentas (rol text PRIMARY KEY, cuenta text NOT NULL) ON COMMIT DROP;
        INSERT INTO con11_cuentas (rol, cuenta) VALUES
            ('PERDIDA_CAMBIARIA_REALIZADA_PROVEEDORES',  '701.01.01.02'),
            ('UTILIDAD_CAMBIARIA_REALIZADA_PROVEEDORES', '702.01.01.02'),
            ('PERDIDA_CAMBIARIA_REALIZADA_CLIENTES',     '701.01.01.01'),
            ('UTILIDAD_CAMBIARIA_REALIZADA_CLIENTES',    '702.01.01.01'),
            ('PERDIDA_CAMBIARIA_NO_REALIZADA_BANCOS',    '701.01.02.03'),
            ('UTILIDAD_CAMBIARIA_NO_REALIZADA_BANCOS',   '702.01.02.03'),
            ('PERDIDA_CAMBIARIA_NO_REALIZADA_CLIENTES',  '701.01.02.01'),
            ('UTILIDAD_CAMBIARIA_NO_REALIZADA_CLIENTES', '702.01.02.01'),
            ('PERDIDA_CAMBIARIA_NO_REALIZADA',           '701.01.02.02'),
            ('UTILIDAD_CAMBIARIA_NO_REALIZADA',          '702.01.02.02');

        CREATE TEMP TABLE con11_por_asignar ON COMMIT DROP AS
        SELECT r.id, r.empresa_id, r.cuenta_id AS anterior, c.id AS cuenta_nueva, cc.cuenta
        FROM contabilidad.roles_cuenta r
        JOIN con11_cuentas cc ON cc.rol = r.codigo
        JOIN contabilidad.cuentas_contables c
          ON c.empresa_id = r.empresa_id AND c.codigo = cc.cuenta AND c.deleted_at IS NULL
         AND c.estatus = 0 /* Activo */ AND c.tipo = 1 /* Afectable */
        LEFT JOIN contabilidad.cuentas_contables actual ON actual.id = r.cuenta_id
        WHERE r.deleted_at IS NULL AND r.sucursal_id IS NULL AND r.moneda IS NULL
          AND (r.vigente_hasta IS NULL OR r.vigente_hasta >= current_date)
          AND r.cuenta_id IS DISTINCT FROM c.id
          AND (r.cuenta_id IS NULL OR actual.codigo LIKE '990.%' OR actual.codigo LIKE 'DEMO%');

        UPDATE contabilidad.roles_cuenta r
           SET cuenta_id = p.cuenta_nueva, version = r.version + 1,
               updated_at = now() AT TIME ZONE 'UTC', updated_by = '{Usuario}'
          FROM con11_por_asignar p
         WHERE r.id = p.id;

        INSERT INTO contabilidad.roles_cuenta_cambios (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
        SELECT gen_random_uuid(), empresa_id, id, anterior, cuenta_nueva, '{Usuario}', now() AT TIME ZONE 'UTC',
               'CON-11: cuenta ' || cuenta || ' del catálogo de Contabilidad del 5-nov (diferencia cambiaria por origen)'
        FROM con11_por_asignar;
        """);

    /// <summary>Regresa cada rol a la cuenta que tenía antes de esta migración, si nadie la cambió después.</summary>
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        UPDATE contabilidad.roles_cuenta r
           SET cuenta_id = b.cuenta_anterior_id, version = r.version + 1,
               updated_at = now() AT TIME ZONE 'UTC', updated_by = '{Usuario}'
          FROM contabilidad.roles_cuenta_cambios b
         WHERE b.usuario = '{Usuario}' AND b.rol_cuenta_id = r.id AND r.cuenta_id = b.cuenta_nueva_id;
        DELETE FROM contabilidad.roles_cuenta_cambios WHERE usuario = '{Usuario}';
        """);
}
