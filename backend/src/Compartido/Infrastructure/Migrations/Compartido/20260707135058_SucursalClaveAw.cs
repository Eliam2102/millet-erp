using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class SucursalClaveAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clave_aw",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sucursales_clave_aw",
                schema: "compartido",
                table: "sucursales",
                column: "clave_aw",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sucursales_clave_aw",
                schema: "compartido",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "clave_aw",
                schema: "compartido",
                table: "sucursales");
        }
    }
}
