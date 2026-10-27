using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class C14PermisosMotorContable : Migration
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
                    { new Guid("0000000d-0007-0000-0000-000000000001"), "leer", "contabilidad.motor.leer", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar los roles de cuenta, las plantillas contables y la bandeja de rechazos del motor de pólizas", "contabilidad", "motor", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000d-0007-0000-0000-000000000002"), "administrar", "contabilidad.roles-cuenta.administrar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Dar de alta roles de cuenta y cambiar la cuenta de un rol (queda en bitácora; solo afecta pólizas nuevas)", "contabilidad", "roles-cuenta", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000d-0007-0000-0000-000000000003"), "gestionar", "contabilidad.rechazos.gestionar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Reprocesar o descartar (con motivo) los eventos rechazados por el motor de pólizas", "contabilidad", "rechazos", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            // C1.4: los roles que ya autorizan pólizas (contabilidad.polizas.autorizar) reciben los tres permisos del motor.
            migrationBuilder.Sql("""
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, created_at, updated_at, created_by, version)
                SELECT gen_random_uuid(), rp.rol_id, p.id, now(), now(), 'seed-c14', 1
                FROM identidad.rol_permisos rp
                CROSS JOIN (VALUES ('0000000d-0007-0000-0000-000000000001'::uuid), ('0000000d-0007-0000-0000-000000000002'::uuid),
                                   ('0000000d-0007-0000-0000-000000000003'::uuid)) AS p(id)
                WHERE rp.permiso_id = '0000000d-0005-0000-0000-000000000003' AND rp.deleted_at IS NULL
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // rol_permisos y usuario_permiso_overrides.permiso_id son FK Restrict.
            migrationBuilder.Sql("DELETE FROM identidad.rol_permisos WHERE permiso_id IN ('0000000d-0007-0000-0000-000000000001','0000000d-0007-0000-0000-000000000002','0000000d-0007-0000-0000-000000000003');");
            migrationBuilder.Sql("DELETE FROM identidad.usuario_permiso_overrides WHERE permiso_id IN ('0000000d-0007-0000-0000-000000000001','0000000d-0007-0000-0000-000000000002','0000000d-0007-0000-0000-000000000003');");
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000d-0007-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000d-0007-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("0000000d-0007-0000-0000-000000000003"));
        }
    }
}
