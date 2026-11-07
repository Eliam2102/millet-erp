using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class C17RetencionesPorConcepto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "retencion_al_pago",
                schema: "contabilidad",
                table: "saldos_iva_documento",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "retenciones",
                schema: "contabilidad",
                table: "saldos_iva_documento",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retenciones",
                schema: "contabilidad",
                table: "reclasificaciones_iva",
                type: "jsonb",
                nullable: true);
            // 86e3zcbjw: saldos anteriores sin retenciones por concepto (y su póliza ya registró la retención: RetencionAlPago = false).
            migrationBuilder.Sql("""
                UPDATE contabilidad.saldos_iva_documento SET retenciones = '[]'::jsonb WHERE retenciones IS NULL;
                UPDATE contabilidad.reclasificaciones_iva SET retenciones = '[]'::jsonb WHERE retenciones IS NULL;
                """);
            // 86e3zcbjw: roles por concepto de retención SIN cuenta (backend/scripts/contabilidad-asignar-cuentas-retenciones.sql los mapea);
            // sin cuenta, el motor usa el rol base.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C17', 'migration-C17', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('RETENCIONES_POR_PAGAR_HONORARIOS_PF_ISR', 'ISR retenido por servicios profesionales'),
                    ('RETENCIONES_POR_PAGAR_HONORARIOS_PF_IVA', 'IVA retenido por honorarios'),
                    ('RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_ISR', 'ISR retenido por arrendamiento'),
                    ('RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_IVA', 'IVA retenido por arrendamiento'),
                    ('RETENCIONES_POR_PAGAR_FLETES_IVA', 'IVA retenido por fletes (4 %)'),
                    ('RETENCIONES_POR_PAGAR_SERVICIOS_IVA', 'IVA retenido por servicios (6 %)'),
                    ('RETENCIONES_POR_PAGAR_RESICO_PF_ISR', 'ISR retenido a RESICO (1.25 %)'),
                    ('RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_ISR', 'ISR retenido a RESICO con servicios (1.25 %)'),
                    ('RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_IVA', 'IVA retenido a RESICO con servicios'),
                    ('RETENCIONES_CLIENTES_FLETES_IVA', 'IVA que nos retienen los clientes por fletes (4 %)'),
                    ('RETENCIONES_CLIENTES_SERVICIOS_IVA', 'IVA que nos retienen los clientes por servicios (6 %)'),
                    ('RETENCIONES_CLIENTES_DESPERDICIO_IVA', 'IVA que nos retienen los clientes por desperdicio (16 %)'),
                    ('RETENCIONES_CLIENTES_IMPORTACION_IVA', 'IVA que nos retienen los clientes por importación (16 %)')) AS r(codigo, descripcion)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);
                """);
            // 86e3zcbjw: plantillas v3 sobre la vigente (no se edita la anterior; solo si la vigente aún no trae líneas de grupo):
            //   #2, #3b, #3c: proveedor por el total antes de retenciones y sin retenciones por pagar (la retención va al pago);
            //   #5: cargo al proveedor por el pago más su retención y abono a la cuenta de cada concepto retenido;
            //   #7: la retención del cliente por concepto (rol por clasificador).
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       left(coalesce(v.descripcion, '') || ' · 86e3zcbjw: retenciones por concepto'
                            || CASE WHEN v.tipo_evento LIKE 'cuentas_por_pagar.%' OR v.tipo_evento LIKE 'tesoreria.%' THEN ' y al pago' ELSE '' END, 500),
                       n.lineas, 1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C17', 'migration-C17', NULL
                FROM (SELECT DISTINCT ON (empresa_id, tipo_evento) *
                      FROM contabilidad.plantillas_contables
                      WHERE deleted_at IS NULL AND tipo_evento IN ('cuentas_por_pagar.factura.registrada.v1',
                          'cuentas_por_pagar.nota-credito.registrada.v1', 'cuentas_por_pagar.nota-cargo.autorizada.v1',
                          'tesoreria.pago-factura-proveedor.aplicado.v1', 'facturacion.factura-venta.timbrada.v1')
                      ORDER BY empresa_id, tipo_evento, numero_version DESC) v
                CROSS JOIN LATERAL (
                    SELECT jsonb_agg(l ORDER BY o) || CASE WHEN v.tipo_evento = 'tesoreria.pago-factura-proveedor.aplicado.v1'
                               THEN jsonb_build_array(jsonb_build_object('Orden', max(o) + 1, 'Rol', 'RETENCIONES_POR_PAGAR', 'Lado', 'Abono',
                                   'Campo', 'RetencionAplicada', 'Auxiliar', 'Ninguno', 'PorLinea', true, 'SoloServicios', NULL,
                                   'ConCentroCosto', false, 'Concepto', 'Retenciones por pagar', 'RolPorClasificador', true, 'Grupo', 'RETENCION'))
                               ELSE '[]'::jsonb END AS lineas
                    FROM (SELECT o, CASE
                                 WHEN x->>'Rol' = 'PROVEEDORES' AND v.tipo_evento LIKE 'cuentas_por_pagar.%' AND x->>'Campo' = 'TotalMenosRetenciones'
                                     THEN jsonb_set(x, '{Campo}', '"TotalMasRetenciones"')
                                 WHEN x->>'Rol' = 'PROVEEDORES' AND v.tipo_evento LIKE 'tesoreria.%' AND x->>'Campo' = 'Total'
                                     THEN jsonb_set(x, '{Campo}', '"TotalMasRetenciones"')
                                 WHEN x->>'Rol' = 'RETENCIONES_CLIENTES'
                                     THEN x || '{"PorLinea": true, "RolPorClasificador": true, "Grupo": "RETENCION"}'::jsonb
                                 ELSE x END AS l
                          FROM jsonb_array_elements(v.lineas) WITH ORDINALITY AS t(x, o)
                          WHERE NOT (x->>'Rol' = 'RETENCIONES_POR_PAGAR' AND v.tipo_evento LIKE 'cuentas_por_pagar.%')) s
                ) n
                WHERE v.lineas::text NOT LIKE '%"Grupo"%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-C17';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-C17' AND cuenta_id IS NULL;
                """);
            migrationBuilder.DropColumn(
                name: "retencion_al_pago",
                schema: "contabilidad",
                table: "saldos_iva_documento");

            migrationBuilder.DropColumn(
                name: "retenciones",
                schema: "contabilidad",
                table: "saldos_iva_documento");

            migrationBuilder.DropColumn(
                name: "retenciones",
                schema: "contabilidad",
                table: "reclasificaciones_iva");
        }
    }
}
