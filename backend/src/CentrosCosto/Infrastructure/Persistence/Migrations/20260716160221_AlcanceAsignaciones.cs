using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CentrosCosto.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlcanceAsignaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asignaciones",
                schema: "centros_costo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dim3_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_asignaciones_dim3_dim3_id",
                        column: x => x.dim3_id,
                        principalSchema: "centros_costo",
                        principalTable: "dim3",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_dim3",
                schema: "centros_costo",
                table: "asignaciones",
                column: "dim3_id");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_usuario",
                schema: "centros_costo",
                table: "asignaciones",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ux_asignaciones_usuario_dim3",
                schema: "centros_costo",
                table: "asignaciones",
                columns: new[] { "usuario_id", "dim3_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignaciones",
                schema: "centros_costo");
        }
    }
}
