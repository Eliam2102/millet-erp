using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class G1PlantillaSalidaInventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // G-1 (86e3yxpb3): salida de inventario sin requisición (consumo, rotura, salida manual, baja por daño). Primera versión = la
            // vigente de la plantilla #14 (cargo CONSUMO por centro de costo · abono INVENTARIO por grupo), con los mismos roles: no se
            // inventa ninguna cuenta. Contabilidad la afina por causa en «Contraste · U-2». Sin cuenta o sin centro de costo, el motor
            // manda el evento a la bandeja de rechazos; nunca lo pierde.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, 'almacen.salida_inventario.registrada.v1', 1, v.vigente_desde, v.tipo_operacion_clave,
                       'Salida de inventario', 'G-1: salida sin requisición a costo, como la plantilla #14 (consumo contra inventario)', v.lineas,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-G1', 'migration-G1', NULL
                FROM (SELECT DISTINCT ON (empresa_id) *
                      FROM contabilidad.plantillas_contables
                      WHERE tipo_evento = 'almacen.salida_requisicion.registrada.v1' AND deleted_at IS NULL
                      ORDER BY empresa_id, numero_version DESC) v
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables p
                                  WHERE p.empresa_id = v.empresa_id AND p.tipo_evento = 'almacen.salida_inventario.registrada.v1');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-G1';");
        }
    }
}
