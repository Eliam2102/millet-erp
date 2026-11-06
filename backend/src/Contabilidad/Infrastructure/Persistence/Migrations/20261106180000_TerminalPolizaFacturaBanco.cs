using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// 86e3zcbjx: plantilla de la factura mensual del banco por las comisiones de la terminal de tarjetas
/// (<c>facturacion.terminal.factura-banco-ligada.v1</c>). Cuentas por operación de Contabilidad del 5-nov: comisión 701.10.00.00
/// (rol COMISIONES_BANCARIAS, ya existente), IVA 118.01.01.00 pagado (rol IVA_ACREDITABLE_PAGADO, ya existente), redondeo 703.21.00.00
/// y la cuenta del banco de la terminal («depende del banco de nuestra TPV»). Los dos roles nuevos nacen SIN cuenta: Contabilidad los
/// mapea en «Roles de cuenta»; sin cuenta, el motor deja el evento en la bandeja y se reprocesa desde ahí. Solo datos: el modelo no cambia.
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261106180000_TerminalPolizaFacturaBanco")]
public sealed class TerminalPolizaFacturaBanco : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO contabilidad.roles_cuenta
            (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-86e3zcbjx', 'migration-86e3zcbjx', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('BANCO_TERMINAL_TARJETA', 'Cuenta del banco de la terminal de tarjetas: de ahí sale la comisión mensual (depende del banco de la TPV)'),
            ('REDONDEO', 'Redondeo de hasta $0.01 entre la factura del banco y sus cargos (sugerida: 703.21.00.00 OTROS GASTOS)')
        ) AS r(codigo, descripcion)
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);

        INSERT INTO contabilidad.plantillas_contables
            (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, p.tipo_evento, 1, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-86e3zcbjx', 'migration-86e3zcbjx', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('facturacion.terminal.factura-banco-ligada.v1', 'AS', 'Comisión de la terminal de tarjetas', 'Factura mensual del banco ligada a los cargos de la terminal: comisión e IVA pagado contra el banco; la diferencia de hasta un centavo, a redondeo', '[{"Orden": 1, "Rol": "COMISIONES_BANCARIAS", "Lado": "Cargo", "Campo": "Comision", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Comisión de la terminal"}, {"Orden": 2, "Rol": "IVA_ACREDITABLE_PAGADO", "Lado": "Cargo", "Campo": "Iva", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "IVA de la comisión"}, {"Orden": 3, "Rol": "REDONDEO", "Lado": "Cargo", "Campo": "Redondeo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Redondeo"}, {"Orden": 4, "Rol": "BANCO_TERMINAL_TARJETA", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cargos del banco por la terminal"}]')
        ) AS p(tipo_evento, clave, concepto, descripcion, lineas)
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x
                          WHERE x.empresa_id = e.id AND x.tipo_evento = p.tipo_evento AND x.numero_version = 1);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-86e3zcbjx';
        DELETE FROM contabilidad.roles_cuenta_cambios WHERE rol_cuenta_id IN (
            SELECT id FROM contabilidad.roles_cuenta WHERE created_by = 'migration-86e3zcbjx');
        DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-86e3zcbjx';
        """);
}
