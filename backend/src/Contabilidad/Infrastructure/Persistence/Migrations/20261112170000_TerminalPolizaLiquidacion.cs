using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// Póliza de la liquidación de la terminal de tarjetas (<c>facturacion.terminal.liquidacion-registrada.v1</c>), que Contabilidad aún no
/// consumía. Contabilidad del 11-nov: la comisión de los cobros con TPV (crédito/débito) va a 601.75 y las demás comisiones bancarias a
/// 701.10. Rol nuevo <c>COMISIONES_TPV</c> (601.75.00.00), SIN cuenta: Contabilidad lo mapea en «Roles de cuenta»; sin cuenta, el motor
/// deja el evento en la bandeja y se reprocesa desde ahí. <c>COMISIONES_BANCARIAS</c> (701.10) no cambia. Solo datos: el modelo no cambia.
/// Plantilla: cargo banco de la terminal por el neto, comisión (601.75) e IVA pendiente de pago; abono «COBROS T/C, T/D» por el bruto.
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261112170000_TerminalPolizaLiquidacion")]
public sealed class TerminalPolizaLiquidacion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO contabilidad.roles_cuenta
            (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-tpv-liquidacion', 'migration-tpv-liquidacion', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('COMISIONES_TPV', 'Comisión de los cobros con terminal (TPV) de crédito y débito (sugerida: 601.75.00.00; las demás comisiones bancarias siguen en 701.10)'),
            ('BANCO_TERMINAL_TARJETA', 'Cuenta del banco de la terminal de tarjetas: de ahí sale la comisión mensual (depende del banco de la TPV)'),
            ('IVA_ACREDITABLE_PENDIENTE', 'IVA acreditable pendiente de pago')
        ) AS r(codigo, descripcion)
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);

        INSERT INTO contabilidad.plantillas_contables
            (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, p.tipo_evento, 1, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-tpv-liquidacion', 'migration-tpv-liquidacion', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('facturacion.terminal.liquidacion-registrada.v1', 'PR', 'Liquidación de la terminal de tarjetas', 'Liquidación del banco por folio: banco por el neto, comisión TPV (601.75) e IVA pendiente contra los cobros con tarjeta por el bruto', '[{"Orden": 1, "Rol": "BANCO_TERMINAL_TARJETA", "Lado": "Cargo", "Campo": "Neto", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Liquidación del banco"}, {"Orden": 2, "Rol": "COMISIONES_TPV", "Lado": "Cargo", "Campo": "Comision", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Comisión de la terminal"}, {"Orden": 3, "Rol": "IVA_ACREDITABLE_PENDIENTE", "Lado": "Cargo", "Campo": "IvaComision", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "IVA de la comisión"}, {"Orden": 4, "Rol": "COBROS_TC_TD", "Lado": "Abono", "Campo": "Bruto", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cobros con tarjeta liquidados"}]')
        ) AS p(tipo_evento, clave, concepto, descripcion, lineas)
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x
                          WHERE x.empresa_id = e.id AND x.tipo_evento = p.tipo_evento AND x.numero_version = 1);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-tpv-liquidacion';
        DELETE FROM contabilidad.roles_cuenta_cambios WHERE rol_cuenta_id IN (
            SELECT id FROM contabilidad.roles_cuenta WHERE created_by = 'migration-tpv-liquidacion');
        DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-tpv-liquidacion' AND cuenta_id IS NULL;
        """);
}
