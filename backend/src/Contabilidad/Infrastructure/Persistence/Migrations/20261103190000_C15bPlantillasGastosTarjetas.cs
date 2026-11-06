using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// C1.5b (86e3vrh7y, E11): plantillas de caja chica, viáticos, tarjetas corporativas y amortización del anticipo a proveedor, y sus
    /// roles SIN cuenta (Contabilidad los mapea en «Roles de cuenta»; V12 pendiente; mientras, cuentas DEMO del sembrador). Solo datos:
    /// el modelo no cambia.
    /// </summary>
    public partial class C15bPlantillasGastosTarjetas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- C1.5b: CONTRAPARTIDA_GASTO es el rol base; el motor usa el de su origen (rol por clasificador) y, si no existe, el base.
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C15b', 'migration-C15b', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('CONTRAPARTIDA_GASTO', 'Contrapartida del gasto comprobado (base; se usa la del origen: caja chica, viáticos o tarjeta)'),
                    ('CONTRAPARTIDA_GASTO_CAJA_CHICA', 'Fondo fijo de caja chica (comprobación y reposición)'),
                    ('CONTRAPARTIDA_GASTO_VIATICOS', 'Deudores empleados por viáticos (préstamo, comprobación y liquidación)'),
                    ('CONTRAPARTIDA_GASTO_TARJETA', 'Tarjetas corporativas por pagar al banco emisor (movimientos y corte)'),
                    ('ACREEDORES_DIVERSOS', 'Acreedores diversos (reposición de caja chica y viáticos por pagar)')
                ) AS r(codigo, descripcion)
                ON CONFLICT DO NOTHING;

                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, p.tipo_evento, 1, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C15b', 'migration-C15b', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('cuentas_por_pagar.gasto.registrado.v1', 'AS', 'Gasto comprobado', 'C1.5b: gasto de caja chica, viáticos o tarjeta; gasto e IVA acreditable contra el fondo, el deudor del empleado o la tarjeta por pagar', '[{"Orden": 1, "Rol": "GASTO", "Lado": "Cargo", "Campo": "Gasto", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Gasto"}, {"Orden": 2, "Rol": "IVA_ACREDITABLE_PAGADO", "Lado": "Cargo", "Campo": "IvaAcreditable", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "IVA acreditable pagado"}, {"Orden": 3, "Rol": "CONTRAPARTIDA_GASTO", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Comprobación del gasto", "RolPorClasificador": true}]'),
                    ('cuentas_por_pagar.pasivo.autorizado-para-pago.v1', 'AS', 'Pasivo interno', 'C1.5b: reposición de caja chica y préstamo o liquidación de viáticos; fondo o deudor contra acreedores diversos (el pasivo de una factura no genera póliza)', '[{"Orden": 1, "Rol": "CONTRAPARTIDA_GASTO", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Reposición o préstamo", "RolPorClasificador": true}, {"Orden": 2, "Rol": "ACREEDORES_DIVERSOS", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Acreedores diversos"}]'),
                    ('cuentas_por_pagar.estado-cuenta-tc.cerrado.v1', 'TT', 'Estado de cuenta de tarjeta', 'C1.5b: cierre del estado de cuenta; la tarjeta por pagar pasa a la factura agregada del banco emisor', '[{"Orden": 1, "Rol": "CONTRAPARTIDA_GASTO_TARJETA", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Tarjeta corporativa por pagar"}, {"Orden": 2, "Rol": "PROVEEDORES", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Banco emisor"}]'),
                    ('cuentas_por_pagar.anticipo.amortizado.v1', 'AN', 'Amortización de anticipo a proveedor', 'C1.5b: aplicación del anticipo a una factura; proveedor contra anticipo a proveedores', '[{"Orden": 1, "Rol": "PROVEEDORES", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Proveedor"}, {"Orden": 2, "Rol": "ANTICIPO_A_PROVEEDORES", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Amortización de anticipo"}]')
                ) AS p(tipo_evento, clave, concepto, descripcion, lineas)
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-C15b';
                DELETE FROM contabilidad.roles_cuenta_cambios WHERE rol_cuenta_id IN (
                    SELECT id FROM contabilidad.roles_cuenta WHERE created_by = 'migration-C15b');
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-C15b';
                """);
        }
    }
}
