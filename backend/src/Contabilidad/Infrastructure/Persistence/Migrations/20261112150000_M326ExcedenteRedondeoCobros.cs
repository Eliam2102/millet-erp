using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M-326 parte 2 (a): el excedente de hasta $0.99 de un cobro (el cliente depositó centavos de más) se abona a la cuenta REDONDEO.
    /// Agrega una versión de la plantilla #12 con la línea REDONDEO al Haber (campo <c>ExcedenteRedondeo</c>), solo si la vigente no la
    /// trae (no pisa una versión editada por Contabilidad). Solo datos: no cambia el modelo.
    /// </summary>
    [DbContext(typeof(ContabilidadDbContext))]
    [Migration("20261112150000_M326ExcedenteRedondeoCobros")]
    public partial class M326ExcedenteRedondeoCobros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       'Plantilla #12 (M-326 parte 2): excedente de hasta $0.99 del cobro, abono a la cuenta REDONDEO',
                       v.lineas::jsonb || '[{"Orden": 6, "Rol": "REDONDEO", "Lado": "Abono", "Campo": "ExcedenteRedondeo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Redondeo del cobro (excedente)"}]'::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-M326b', 'migration-M326b', NULL
                FROM (SELECT DISTINCT ON (empresa_id) *
                      FROM contabilidad.plantillas_contables
                      WHERE tipo_evento = 'tesoreria.pago-cliente.confirmado.v1' AND deleted_at IS NULL
                      ORDER BY empresa_id, numero_version DESC) v
                WHERE v.lineas::text LIKE '%"Aplicado"%' AND v.lineas::text NOT LIKE '%ExcedenteRedondeo%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-M326b';");
        }
    }
}
