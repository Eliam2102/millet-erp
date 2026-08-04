using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RolGruposEntraId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rol_grupos_entra_id",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rol_grupos_entra_id", x => x.id);
                    table.ForeignKey(
                        name: "fk_rol_grupos_entra_id_roles_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "identidad",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rol_grupos_entra_id_rol_id_object_id",
                schema: "identidad",
                table: "rol_grupos_entra_id",
                columns: new[] { "rol_id", "object_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rol_grupos_entra_id",
                schema: "identidad");
        }
    }
}
