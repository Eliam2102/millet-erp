using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M284PermisoAdoptarCfdi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[] { new Guid("00000009-000b-0000-0000-000000000003"), "adoptar-cfdi", "facturacion.comprobantes.adoptar-cfdi", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Adoptar el CFDI que el PAC sí timbró (XML del portal) en un comprobante cuyo timbrado no se confirmó", "facturacion", "comprobantes", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 });

            // M-284: quien ya puede reintentar el timbrado recibe también adoptar el CFDI timbrado.
            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, created_at, updated_at, created_by, version)
                SELECT DISTINCT ON (rp.rol_id) gen_random_uuid(), rp.rol_id, '00000009-000b-0000-0000-000000000003', now(), now(), 'seed-86e3zr751', 1
                FROM identidad.rol_permisos rp
                WHERE rp.permiso_id = '00000009-000b-0000-0000-000000000001' AND rp.deleted_at IS NULL
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM identidad.rol_permisos WHERE permiso_id = '00000009-000b-0000-0000-000000000003' AND created_by = 'seed-86e3zr751';");
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000009-000b-0000-0000-000000000003"));
        }
    }
}
