using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AduanaPedimentoExportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "aduana_pedimento_exportacion",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aduana_pedimento_exportacion",
                schema: "comercio_exterior",
                table: "expedientes");
        }
    }
}
