using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProveedorClaveAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "clave_aw",
                schema: "compartido",
                table: "proveedores",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_proveedores_clave_aw",
                schema: "compartido",
                table: "proveedores",
                column: "clave_aw",
                unique: true,
                filter: "clave_aw IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proveedores_clave_aw_positiva",
                schema: "compartido",
                table: "proveedores",
                sql: "clave_aw IS NULL OR clave_aw > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_proveedores_clave_aw",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropCheckConstraint(
                name: "ck_proveedores_clave_aw_positiva",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "clave_aw",
                schema: "compartido",
                table: "proveedores");
        }
    }
}
