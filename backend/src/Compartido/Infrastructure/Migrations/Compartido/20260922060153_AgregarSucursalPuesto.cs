using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AgregarSucursalPuesto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sucursal_puestos",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    puesto_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_sucursal_puestos", x => x.id);
                    table.CheckConstraint("ck_sucursal_puestos_estatus", "estatus BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_sucursal_puestos_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalSchema: "compartido",
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sucursal_puestos_puestos_puesto_id",
                        column: x => x.puesto_id,
                        principalSchema: "compartido",
                        principalTable: "puestos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sucursal_puestos_sucursales_sucursal_id",
                        column: x => x.sucursal_id,
                        principalSchema: "compartido",
                        principalTable: "sucursales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_puestos_empresa_id",
                schema: "compartido",
                table: "sucursal_puestos",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_puestos_puesto_id",
                schema: "compartido",
                table: "sucursal_puestos",
                column: "puesto_id");

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_puestos_sucursal_id",
                schema: "compartido",
                table: "sucursal_puestos",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ix_sucursal_puestos_sucursal_id_puesto_id",
                schema: "compartido",
                table: "sucursal_puestos",
                columns: new[] { "sucursal_id", "puesto_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sucursal_puestos",
                schema: "compartido");
        }
    }
}
