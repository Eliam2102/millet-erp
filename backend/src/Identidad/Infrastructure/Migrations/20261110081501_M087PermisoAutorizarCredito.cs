using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M087PermisoAutorizarCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000009-0004-0000-0000-000000000005"), "autorizar-credito", "facturacion.notas-credito.autorizar-credito", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Firmar por Crédito y Cobranza las notas de crédito de A+W arriba del umbral de la segunda firma (35,000 MXN por omisión)", "facturacion", "notas-credito", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            // M-087: la firma de Crédito y Cobranza la reciben los roles que gestionan líneas de crédito.
            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, created_at, updated_at, created_by, version)
                SELECT DISTINCT ON (rp.rol_id) gen_random_uuid(), rp.rol_id, '00000009-0004-0000-0000-000000000005', now(), now(), 'seed-86e3zr5wu', 1
                FROM identidad.rol_permisos rp
                WHERE rp.permiso_id = '0000000a-0001-0000-0000-000000000002' AND rp.deleted_at IS NULL
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM identidad.rol_permisos WHERE permiso_id = '00000009-0004-0000-0000-000000000005' AND created_by = 'seed-86e3zr5wu';");
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000009-0004-0000-0000-000000000005"));
        }
    }
}
