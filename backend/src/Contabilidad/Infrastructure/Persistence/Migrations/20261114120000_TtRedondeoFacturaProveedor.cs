using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Póliza TT de la factura de proveedor: la base va neta de descuentos y lo que difiera la suma de las líneas del total del CFDI
    /// (tolerancia de captura de hasta $0.99 y centavos del prorrateo del descuento global) va a la cuenta REDONDEO, igual que el pago
    /// (M-326). Sin esas líneas la TT no cuadraba, quedaba rechazada y el pago de la factura esperaba su póliza para siempre. Agrega una
    /// versión de la plantilla #2 con REDONDEO al Debe (<c>RedondeoCargo</c>) y al Haber (<c>RedondeoAbono</c>), solo si la vigente aún
    /// no las trae, para no pisar una versión editada. Solo datos: no cambia el modelo. El rol REDONDEO ya existe (M-326, sin cuenta: la
    /// asigna Contabilidad); en cero no exige cuenta.
    /// </summary>
    [DbContext(typeof(ContabilidadDbContext))]
    [Migration("20261114120000_TtRedondeoFacturaProveedor")]
    public partial class TtRedondeoFacturaProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       'Plantilla #2: base neta de descuentos; diferencia de hasta $0.99 contra el total del CFDI a la cuenta REDONDEO',
                       v.lineas::jsonb || jsonb_build_array(
                           jsonb_build_object('Orden', m.orden + 1, 'Rol', 'REDONDEO', 'Lado', 'Cargo', 'Campo', 'RedondeoCargo', 'Auxiliar', 'Ninguno',
                                              'PorLinea', false, 'SoloServicios', NULL, 'ConCentroCosto', false, 'Concepto', 'Redondeo de la factura'),
                           jsonb_build_object('Orden', m.orden + 2, 'Rol', 'REDONDEO', 'Lado', 'Abono', 'Campo', 'RedondeoAbono', 'Auxiliar', 'Ninguno',
                                              'PorLinea', false, 'SoloServicios', NULL, 'ConCentroCosto', false, 'Concepto', 'Redondeo de la factura')),
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-TT-redondeo', 'migration-TT-redondeo', NULL
                FROM (SELECT DISTINCT ON (empresa_id) *
                      FROM contabilidad.plantillas_contables
                      WHERE tipo_evento = 'cuentas_por_pagar.factura.registrada.v1' AND deleted_at IS NULL
                      ORDER BY empresa_id, numero_version DESC) v
                CROSS JOIN LATERAL (SELECT coalesce(max((x->>'Orden')::int), 0) AS orden FROM jsonb_array_elements(v.lineas::jsonb) x) m
                WHERE v.lineas::text NOT LIKE '%RedondeoCargo%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-TT-redondeo';");
        }
    }
}
