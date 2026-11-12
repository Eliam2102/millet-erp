using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M349TerminalAfiliacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "afiliacion",
                schema: "facturacion",
                table: "terminal_transaccion",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cod_terminal",
                schema: "facturacion",
                table: "terminal_transaccion",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "afiliacion",
                schema: "facturacion",
                table: "terminal_transaccion");

            migrationBuilder.DropColumn(
                name: "cod_terminal",
                schema: "facturacion",
                table: "terminal_transaccion");
        }
    }
}
