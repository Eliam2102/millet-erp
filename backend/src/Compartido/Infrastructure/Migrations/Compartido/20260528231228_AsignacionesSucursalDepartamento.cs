using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AsignacionesSucursalDepartamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sucursal_departamentos",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    departamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sucursal_departamentos", x => x.id);
                    table.CheckConstraint("ck_sucursal_departamentos_estatus", "estatus BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_sucursal_departamentos_departamentos_departamento_id",
                        column: x => x.departamento_id,
                        principalSchema: "compartido",
                        principalTable: "departamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sucursal_departamentos_sucursales_sucursal_id",
                        column: x => x.sucursal_id,
                        principalSchema: "compartido",
                        principalTable: "sucursales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_departamentos_departamento_id",
                schema: "compartido",
                table: "sucursal_departamentos",
                column: "departamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_departamentos_sucursal_id",
                schema: "compartido",
                table: "sucursal_departamentos",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_departamentos_sucursal_id_departamento_id",
                schema: "compartido",
                table: "sucursal_departamentos",
                columns: new[] { "sucursal_id", "departamento_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sucursal_departamentos",
                schema: "compartido");
        }
    }
}
