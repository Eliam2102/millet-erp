using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsuarioServicio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuario_servicio",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    entra_app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entra_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_servicio", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_servicio_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalSchema: "compartido",
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usuario_servicio_permiso",
                schema: "identidad",
                columns: table => new
                {
                    usuario_servicio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permiso_clave = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_servicio_permiso", x => new { x.usuario_servicio_id, x.permiso_clave });
                    table.ForeignKey(
                        name: "fk_usuario_servicio_permiso_usuario_servicio_usuario_servicio_",
                        column: x => x.usuario_servicio_id,
                        principalSchema: "identidad",
                        principalTable: "usuario_servicio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000006-0001-0000-0000-000000000001"), "cotizaciones", "integraciones.aw.cotizaciones.crear", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Enviar cotizaciones EDI desde Glass Agent al ERP para correlación con A+W", "integraciones", "aw", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000006-0001-0000-0000-000000000002"), "cotizaciones", "integraciones.aw.cotizaciones.consultar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar el estado de correlación de una cotización enviada", "integraciones", "aw", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000006-0001-0000-0000-000000000003"), "cotizaciones", "integraciones.aw.cotizaciones.reintentar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Forzar el reintento de drop o de correlación de una cotización", "integraciones", "aw", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000006-0002-0000-0000-000000000001"), "pedidos", "integraciones.aw.pedidos.consultar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar pedidos espejo de A+W vía Hybrid Connection (read-only)", "integraciones", "aw", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000006-0003-0000-0000-000000000001"), "clientes", "integraciones.aw.clientes.consultar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar el catálogo de clientes de A+W (read-only)", "integraciones", "aw", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000006-0004-0000-0000-000000000001"), "articulos", "integraciones.aw.articulos.consultar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar el catálogo de artículos de A+W (read-only)", "integraciones", "aw", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000006-0005-0000-0000-000000000001"), "inventario", "integraciones.aw.inventario.consultar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar inventario de A+W (read-only) para validación de stock", "integraciones", "aw", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000006-0006-0000-0000-000000000001"), "administracion", "integraciones.aw.administracion.servicios", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Administrar el catálogo de service principals (UsuarioServicio)", "integraciones", "aw", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000006-0006-0000-0000-000000000002"), "administracion", "integraciones.aw.administracion.configuracion", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Administrar la configuración del módulo Integraciones.Aw", "integraciones", "aw", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_usuario_servicio_empresa_id",
                schema: "identidad",
                table: "usuario_servicio",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_servicio_entra_app_id",
                schema: "identidad",
                table: "usuario_servicio",
                column: "entra_app_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_servicio_entra_object_id",
                schema: "identidad",
                table: "usuario_servicio",
                column: "entra_object_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usuario_servicio_permiso",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "usuario_servicio",
                schema: "identidad");

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000006-0002-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000006-0003-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000006-0004-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000006-0005-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000006-0006-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "identidad",
                table: "permisos",
                keyColumn: "id",
                keyValue: new Guid("00000006-0006-0000-0000-000000000002"));
        }
    }
}
