using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReppRecibidoYMotivoTesoreria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "repp_recibido",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // F9-PR1: seed del motivo de revisión usado cuando Tesorería
            // emite CancelacionPasivoSolicitadaEvent. El insert se hace
            // via SeedMotivos en MotivoRevisionConfiguration → la
            // siguiente regeneración de migración lo aplica. Aquí solo
            // garantizamos que existe con un upsert idempotente.
            migrationBuilder.Sql(@"
                INSERT INTO cuentas_por_pagar.motivos_revision (
                    id, codigo, nombre, descripcion, sla_dias,
                    dependencia_revisora_default_codigo, activo,
                    created_at, updated_at, created_by, updated_by, version
                ) VALUES (
                    '00000007-1001-0000-0000-000000000099',
                    'TESORERIA_SOLICITA_CANCELAR',
                    'Tesorería solicita cancelar',
                    'Tesorería detectó razón para cancelar el pasivo (ej. NC fiscal posterior). CxP revisa.',
                    3,
                    'CXP',
                    true,
                    NOW(), NOW(), 'seed', NULL, 1
                )
                ON CONFLICT (id) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM cuentas_por_pagar.motivos_revision
                WHERE id = '00000007-1001-0000-0000-000000000099';");

            migrationBuilder.DropColumn(
                name: "repp_recibido",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");
        }
    }
}
