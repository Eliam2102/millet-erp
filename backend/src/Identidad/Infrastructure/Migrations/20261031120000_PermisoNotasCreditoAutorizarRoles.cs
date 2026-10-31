using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PermisoNotasCreditoAutorizarRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 86e3wjt5r (supuesto, P-195 pendiente): quien ya otorga override de crédito (gerente,
            // cuentas_por_cobrar.liberacion.override) o emite NC por bonificación (administración de Facturación)
            // recibe facturacion.notas-credito.autorizar. La reemplaza la cadena por monto F6.4.
            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, created_at, updated_at, created_by, version)
                SELECT DISTINCT ON (rp.rol_id) gen_random_uuid(), rp.rol_id, '00000009-0004-0000-0000-000000000004', now(), now(), 'seed-86e3wjt5r', 1
                FROM identidad.rol_permisos rp
                WHERE rp.permiso_id IN ('0000000a-0002-0000-0000-000000000002', '00000009-0004-0000-0000-000000000001')
                  AND rp.deleted_at IS NULL
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM identidad.rol_permisos WHERE permiso_id = '00000009-0004-0000-0000-000000000004' AND created_by = 'seed-86e3wjt5r';");
        }
    }
}
