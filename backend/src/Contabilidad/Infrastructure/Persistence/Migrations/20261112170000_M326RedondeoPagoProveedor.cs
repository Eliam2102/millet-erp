using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M-326 parte 2 (b): el pago a proveedor con redondeo autorizado de hasta $0.99 (±). Agrega una versión de la plantilla #5 donde el
    /// abono a BANCOS sale por el efectivo pagado (<c>EfectivoPagado</c>) y la diferencia va a la cuenta REDONDEO (abono si se pagó menos,
    /// cargo si se pagó de más). Solo si la vigente aún no la trae, para no pisar una versión editada. Solo datos: no cambia el modelo.
    /// </summary>
    [DbContext(typeof(ContabilidadDbContext))]
    [Migration("20261112170000_M326RedondeoPagoProveedor")]
    public partial class M326RedondeoPagoProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       'Plantilla #5 (M-326 parte 2): bancos por el efectivo pagado; redondeo de hasta $0.99 a la cuenta REDONDEO',
                       (SELECT jsonb_agg(CASE WHEN x->>'Rol' = 'BANCOS' AND x->>'Campo' = 'Total' THEN jsonb_set(x, '{Campo}', '"EfectivoPagado"') ELSE x END
                                         ORDER BY (x->>'Orden')::int)
                        FROM jsonb_array_elements(v.lineas::jsonb) x)
                       || jsonb_build_array(
                           jsonb_build_object('Orden', m.orden + 1, 'Rol', 'REDONDEO', 'Lado', 'Abono', 'Campo', 'RedondeoAbono', 'Auxiliar', 'Ninguno',
                                              'PorLinea', false, 'SoloServicios', NULL, 'ConCentroCosto', false, 'Concepto', 'Redondeo del pago (a favor)'),
                           jsonb_build_object('Orden', m.orden + 2, 'Rol', 'REDONDEO', 'Lado', 'Cargo', 'Campo', 'RedondeoCargo', 'Auxiliar', 'Ninguno',
                                              'PorLinea', false, 'SoloServicios', NULL, 'ConCentroCosto', false, 'Concepto', 'Redondeo del pago (en contra)')),
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-M326c', 'migration-M326c', NULL
                FROM (SELECT DISTINCT ON (empresa_id) *
                      FROM contabilidad.plantillas_contables
                      WHERE tipo_evento = 'tesoreria.pago-factura-proveedor.aplicado.v1' AND deleted_at IS NULL
                      ORDER BY empresa_id, numero_version DESC) v
                CROSS JOIN LATERAL (SELECT coalesce(max((x->>'Orden')::int), 0) AS orden FROM jsonb_array_elements(v.lineas::jsonb) x) m
                WHERE v.lineas::text NOT LIKE '%EfectivoPagado%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-M326c';");
        }
    }
}
