using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M209IvaPorTratamiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tratamientos",
                schema: "contabilidad",
                table: "saldos_iva_documento",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tratamientos",
                schema: "contabilidad",
                table: "reclasificaciones_iva",
                type: "jsonb",
                nullable: true);
            // M-209 (86e3zr7mp): saldos y reclasificaciones anteriores, todo al 16 % (base = IVA ÷ 0.16), como los trataba el motor.
            migrationBuilder.Sql("""
                UPDATE contabilidad.saldos_iva_documento
                SET tratamientos = CASE WHEN iva <> 0 THEN jsonb_build_array(jsonb_build_object(
                        'Tratamiento', 'TASA16', 'Base', round(iva / 0.16, 2), 'Iva', iva,
                        'BaseAplicada', CASE WHEN base > 0 THEN round(round(iva / 0.16, 2) * aplicado / base, 2) ELSE 0 END,
                        'IvaReclasificado', iva_reclasificado))
                    ELSE '[]'::jsonb END
                WHERE tratamientos IS NULL;
                UPDATE contabilidad.reclasificaciones_iva
                SET tratamientos = CASE WHEN iva <> 0 THEN jsonb_build_array(jsonb_build_object(
                        'Tratamiento', 'TASA16', 'Base', round(iva / 0.16, 2), 'Iva', iva,
                        'BaseMxn', round(iva_mxn / 0.16, 2), 'IvaMxn', iva_mxn))
                    ELSE '[]'::jsonb END
                WHERE tratamientos IS NULL;
                """);
            // M-209: roles por tasa SIN cuenta; sin cuenta, el motor usa el rol base (la separación queda en el saldo para la DIOT).
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-M209', 'migration-M209', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('IVA_ACREDITABLE_PENDIENTE_TASA16', 'IVA acreditable pendiente de pago al 16 %'),
                    ('IVA_ACREDITABLE_PENDIENTE_TASA8', 'IVA acreditable pendiente de pago al 8 % (zona fronteriza)'),
                    ('IVA_TRASLADADO_NO_COBRADO_TASA16', 'IVA trasladado no cobrado al 16 %'),
                    ('IVA_TRASLADADO_NO_COBRADO_TASA8', 'IVA trasladado no cobrado al 8 % (zona fronteriza)')) AS r(codigo, descripcion)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);
                """);
            // M-209: versión siguiente de las plantillas con IVA pendiente o no cobrado (no se edita la anterior): esa línea pasa a una
            // partida por tratamiento (grupo IVA, rol por clasificador). Documentos: #2, #3b, #3c y #7; pagos y cobros que reclasifican:
            // #5, #12, #11a y #11b. Sin líneas del grupo en el evento, la plantilla sigue usando el IVA total en el rol base.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       left(coalesce(v.descripcion, '') || ' · M-209: IVA por tratamiento', 500),
                       n.lineas, 1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-M209', 'migration-M209', NULL
                FROM (SELECT DISTINCT ON (empresa_id, tipo_evento) *
                      FROM contabilidad.plantillas_contables
                      WHERE deleted_at IS NULL AND tipo_evento IN ('cuentas_por_pagar.factura.registrada.v1',
                          'cuentas_por_pagar.nota-credito.registrada.v1', 'cuentas_por_pagar.nota-cargo.autorizada.v1',
                          'facturacion.factura-venta.timbrada.v1', 'tesoreria.pago-factura-proveedor.aplicado.v1',
                          'tesoreria.pago-cliente.confirmado.v1', 'facturacion.cobro-mostrador.registrado.v1',
                          'facturacion.caja-sesion.cerrada.v1')
                      ORDER BY empresa_id, tipo_evento, numero_version DESC) v
                CROSS JOIN LATERAL (
                    SELECT jsonb_agg(CASE WHEN x->>'Rol' IN ('IVA_ACREDITABLE_PENDIENTE', 'IVA_TRASLADADO_NO_COBRADO')
                                               AND x->>'Campo' IN ('Iva', 'IvaReclasificado')
                                          THEN x || '{"PorLinea": true, "RolPorClasificador": true, "Grupo": "IVA"}'::jsonb
                                          ELSE x END ORDER BY o) AS lineas
                    FROM jsonb_array_elements(v.lineas) WITH ORDINALITY AS t(x, o)
                ) n
                WHERE EXISTS (SELECT 1 FROM jsonb_array_elements(v.lineas) y
                              WHERE y->>'Rol' IN ('IVA_ACREDITABLE_PENDIENTE', 'IVA_TRASLADADO_NO_COBRADO')
                                AND y->>'Campo' IN ('Iva', 'IvaReclasificado'))
                  AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(v.lineas) y WHERE y->>'Grupo' = 'IVA');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-M209';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-M209' AND cuenta_id IS NULL;
                """);
            migrationBuilder.DropColumn(
                name: "tratamientos",
                schema: "contabilidad",
                table: "saldos_iva_documento");

            migrationBuilder.DropColumn(
                name: "tratamientos",
                schema: "contabilidad",
                table: "reclasificaciones_iva");
        }
    }
}
