using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M327PermisosReversaComprobacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("0000000e-0006-0000-0000-000000000004"), "revertir-comprobacion-solicitar", "comercio_exterior.anticipos.revertir-comprobacion-solicitar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Solicitar con motivo la reversa de un anticipo al agente ya comprobado", "comercio_exterior", "anticipos", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000e-0006-0000-0000-000000000005"), "revertir-comprobacion-autorizar", "comercio_exterior.anticipos.revertir-comprobacion-autorizar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Autorizar la reversa de un anticipo al agente ya comprobado contra su saldo a favor (Contador General; no quien la solicitó)", "comercio_exterior", "anticipos", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            // M-327: solicitar la reversa lo reciben los roles que ya gestionan anticipos al agente (Comercio exterior).
            // Autorizarla, el Contador General (Millet, 10-nov-2026), con consulta de expedientes de todas las sucursales.
            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, created_at, updated_at, created_by, version)
                SELECT DISTINCT ON (rp.rol_id) gen_random_uuid(), rp.rol_id, '0000000e-0006-0000-0000-000000000004', now(), now(), 'seed-m327', 1
                FROM identidad.rol_permisos rp
                WHERE rp.permiso_id = '0000000e-0006-0000-0000-000000000003' AND rp.deleted_at IS NULL
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, created_at, updated_at, created_by, version)
                SELECT gen_random_uuid(), r.id, p.id, now(), now(), 'seed-m327', 1
                FROM identidad.roles r
                CROSS JOIN (VALUES ('0000000e-0006-0000-0000-000000000005'::uuid), ('0000000e-0001-0000-0000-000000000001'::uuid),
                                   ('0000000e-0002-0000-0000-000000000001'::uuid), ('0000000e-0002-0000-0000-000000000002'::uuid)) AS p(id)
                WHERE (r.codigo = 'contador-general' OR r.nombre = 'Contador General') AND r.deleted_at IS NULL
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM identidad.rol_permisos WHERE created_by = 'seed-m327';");
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0006-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000e-0006-0000-0000-000000000005"));
        }
    }
}
