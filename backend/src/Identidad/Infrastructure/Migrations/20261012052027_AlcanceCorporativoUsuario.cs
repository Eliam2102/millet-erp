using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlcanceCorporativoUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuario_empresa_alcances",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    es_corporativo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_empresa_alcances", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_empresa_alcances_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalSchema: "compartido",
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usuario_empresa_alcances_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "identidad",
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usuario_empresa_alcances_empresa_id",
                schema: "identidad",
                table: "usuario_empresa_alcances",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_empresa_alcances_usuario_id_empresa_id",
                schema: "identidad",
                table: "usuario_empresa_alcances",
                columns: new[] { "usuario_id", "empresa_id" },
                unique: true);
            // Homologa las cuentas corporativas ya identificadas: empleados sin
            // adscripción y administradores de empresa. No altera sus roles.
            migrationBuilder.Sql("""
                INSERT INTO identidad.usuario_empresa_alcances
                    (id, usuario_id, empresa_id, es_corporativo, version, created_at, updated_at, created_by, updated_by)
                SELECT gen_random_uuid(), a.usuario_id, a.empresa_id, true, 1, now(), now(),
                    'MigracionAlcanceCorporativo', 'MigracionAlcanceCorporativo'
                FROM identidad.usuario_empresa_roles a
                JOIN identidad.usuarios u ON u.id = a.usuario_id AND u.activo
                JOIN identidad.roles r ON r.id = a.rol_id AND r.activo
                WHERE r.codigo = 'super-admin' OR EXISTS (
                    SELECT 1 FROM compartido.empleados e WHERE e.usuario_id = a.usuario_id
                        AND e.empresa_id = a.empresa_id AND e.sucursal_id IS NULL AND e.estatus = 0)
                GROUP BY a.usuario_id, a.empresa_id
                ON CONFLICT (usuario_id, empresa_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usuario_empresa_alcances",
                schema: "identidad");
        }
    }
}
