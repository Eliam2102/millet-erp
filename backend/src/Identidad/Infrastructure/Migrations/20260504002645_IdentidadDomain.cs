using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdentidadDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identidad");

            migrationBuilder.CreateTable(
                name: "permisos",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    modulo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    recurso = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    accion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permisos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    es_del_sistema = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entra_oid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "restricciones_rol",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    razon = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    habilitada = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restricciones_rol", x => x.id);
                    table.ForeignKey(
                        name: "fk_restricciones_rol_roles_rol_a_id",
                        column: x => x.rol_a_id,
                        principalSchema: "identidad",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_restricciones_rol_roles_rol_b_id",
                        column: x => x.rol_b_id,
                        principalSchema: "identidad",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rol_permisos",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permiso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rol_permisos", x => x.id);
                    table.ForeignKey(
                        name: "fk_rol_permisos_permisos_permiso_id",
                        column: x => x.permiso_id,
                        principalSchema: "identidad",
                        principalTable: "permisos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rol_permisos_roles_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "identidad",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_empresa_roles",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_empresa_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_empresa_roles_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalSchema: "compartido",
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usuario_empresa_roles_roles_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "identidad",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usuario_empresa_roles_usuarios_asignado_por_usuario_id",
                        column: x => x.asignado_por_usuario_id,
                        principalSchema: "identidad",
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usuario_empresa_roles_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "identidad",
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_preferencias",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ultima_empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idioma_ui = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    zona_horaria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tema_ui = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_preferencias", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_preferencias_empresas_ultima_empresa_id",
                        column: x => x.ultima_empresa_id,
                        principalSchema: "compartido",
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_usuario_preferencias_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "identidad",
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identidad",
                table: "permisos",
                columns: new[] { "id", "accion", "codigo", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "recurso", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000002-0001-0000-0000-000000000001"), "leer", "infra.health.leer", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Leer health checks del sistema", "infra", "health", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000002"), "leer", "infra.audit_log.leer", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Leer el log de auditoría de cualquier módulo", "infra", "audit_log", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000001"), "leer", "identidad.usuarios.leer", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Listar y consultar usuarios", "identidad", "usuarios", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000002"), "crear", "identidad.usuarios.crear", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Crear nuevos usuarios", "identidad", "usuarios", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000003"), "editar", "identidad.usuarios.editar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Editar perfil y estado de usuarios", "identidad", "usuarios", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000004"), "leer", "identidad.roles.leer", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Listar y consultar roles", "identidad", "roles", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000005"), "administrar", "identidad.roles.administrar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Crear, editar y desactivar roles", "identidad", "roles", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000006"), "leer", "identidad.asignaciones.leer", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Consultar asignaciones de roles a usuarios por empresa", "identidad", "asignaciones", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000007"), "administrar", "identidad.asignaciones.administrar", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Asignar y revocar roles a usuarios por empresa", "identidad", "asignaciones", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_permisos_codigo",
                schema: "identidad",
                table: "permisos",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permisos_modulo",
                schema: "identidad",
                table: "permisos",
                column: "modulo");

            migrationBuilder.CreateIndex(
                name: "ix_restricciones_rol_rol_a_id_rol_b_id",
                schema: "identidad",
                table: "restricciones_rol",
                columns: new[] { "rol_a_id", "rol_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_restricciones_rol_rol_b_id",
                schema: "identidad",
                table: "restricciones_rol",
                column: "rol_b_id");

            migrationBuilder.CreateIndex(
                name: "ix_rol_permisos_permiso_id",
                schema: "identidad",
                table: "rol_permisos",
                column: "permiso_id");

            migrationBuilder.CreateIndex(
                name: "ix_rol_permisos_rol_id_permiso_id",
                schema: "identidad",
                table: "rol_permisos",
                columns: new[] { "rol_id", "permiso_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_codigo",
                schema: "identidad",
                table: "roles",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_empresa_roles_asignado_por_usuario_id",
                schema: "identidad",
                table: "usuario_empresa_roles",
                column: "asignado_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_empresa_roles_empresa_id_usuario_id",
                schema: "identidad",
                table: "usuario_empresa_roles",
                columns: new[] { "empresa_id", "usuario_id" });

            migrationBuilder.CreateIndex(
                name: "ix_usuario_empresa_roles_rol_id",
                schema: "identidad",
                table: "usuario_empresa_roles",
                column: "rol_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_empresa_roles_usuario_id_empresa_id_rol_id",
                schema: "identidad",
                table: "usuario_empresa_roles",
                columns: new[] { "usuario_id", "empresa_id", "rol_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_preferencias_ultima_empresa_id",
                schema: "identidad",
                table: "usuario_preferencias",
                column: "ultima_empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_preferencias_usuario_id",
                schema: "identidad",
                table: "usuario_preferencias",
                column: "usuario_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                schema: "identidad",
                table: "usuarios",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_entra_oid",
                schema: "identidad",
                table: "usuarios",
                column: "entra_oid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "restricciones_rol",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "rol_permisos",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "usuario_empresa_roles",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "usuario_preferencias",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "permisos",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "usuarios",
                schema: "identidad");
        }
    }
}
