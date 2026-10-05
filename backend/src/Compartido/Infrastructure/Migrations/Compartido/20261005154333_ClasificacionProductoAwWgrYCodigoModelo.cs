using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ClasificacionProductoAwWgrYCodigoModelo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "familia_codigo",
                schema: "compartido",
                table: "producto_aw",
                newName: "codigo_modelo");

            migrationBuilder.AddColumn<string>(
                name: "wgr",
                schema: "compartido",
                table: "producto_aw",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "wgr_descripcion",
                schema: "compartido",
                table: "producto_aw",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "wgr",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "wgr_descripcion",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.RenameColumn(
                name: "codigo_modelo",
                schema: "compartido",
                table: "producto_aw",
                newName: "familia_codigo");
        }
    }
}
