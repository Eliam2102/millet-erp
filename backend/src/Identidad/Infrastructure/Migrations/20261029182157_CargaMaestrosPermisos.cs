using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CargaMaestrosPermisos : Migration
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
                    { new Guid("00000004-0090-0000-0000-000000000001"), "cargar", "datos_maestros.clientes.cargar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Cargar desde Excel o CSV: clientes", "datos_maestros", "clientes", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000004-0090-0000-0000-000000000002"), "cargar", "datos_maestros.articulos.cargar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Cargar desde Excel o CSV: articulos", "datos_maestros", "articulos", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000004-0090-0000-0000-000000000003"), "cargar", "datos_maestros.proveedores.cargar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Cargar desde Excel o CSV: proveedores", "datos_maestros", "proveedores", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000004-0090-0000-0000-000000000004"), "cargar", "datos_maestros.productos-aw-fiscal.cargar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Cargar desde Excel o CSV: productos-aw-fiscal", "datos_maestros", "productos-aw-fiscal", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000004-0090-0000-0000-000000000005"), "cargar", "centros_costo.catalogo.cargar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Cargar desde Excel o CSV: catalogo", "centros_costo", "catalogo", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });
            // Sólo datos de identidad: los roles que administran cada catálogo reciben su permiso de carga.
            migrationBuilder.Sql("""
                WITH mapa(gestionar, cargar) AS (VALUES
                    ('datos_maestros.clientes.gestionar', 'datos_maestros.clientes.cargar'),
                    ('compartido.catalogos.administrar', 'datos_maestros.articulos.cargar'),
                    ('compartido.catalogos.administrar', 'datos_maestros.proveedores.cargar'),
                    ('datos_maestros.productos-aw.gestionar', 'datos_maestros.productos-aw-fiscal.cargar'),
                    ('centros_costo.catalogo.administrar', 'centros_costo.catalogo.cargar'))
                INSERT INTO identidad.rol_permisos (id, rol_id, permiso_id, version, created_at, updated_at, created_by, updated_by)
                SELECT gen_random_uuid(), rp.rol_id, nuevo.id, 1, now(), now(), 'seed', 'seed'
                FROM mapa m
                JOIN identidad.permisos anterior ON anterior.codigo = m.gestionar
                JOIN identidad.rol_permisos rp ON rp.permiso_id = anterior.id AND rp.deleted_at IS NULL
                JOIN identidad.permisos nuevo ON nuevo.codigo = m.cargar
                ON CONFLICT (rol_id, permiso_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM identidad.rol_permisos
                WHERE permiso_id IN (SELECT id FROM identidad.permisos WHERE codigo IN (
                    'datos_maestros.clientes.cargar', 'datos_maestros.articulos.cargar', 'datos_maestros.proveedores.cargar',
                    'datos_maestros.productos-aw-fiscal.cargar', 'centros_costo.catalogo.cargar'));
                """);
            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000004-0090-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000004-0090-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000004-0090-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000004-0090-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000004-0090-0000-0000-000000000005"));
        }
    }
}
