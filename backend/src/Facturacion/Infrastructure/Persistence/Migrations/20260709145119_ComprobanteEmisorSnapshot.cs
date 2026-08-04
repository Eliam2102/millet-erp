using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComprobanteEmisorSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "lugar_expedicion",
                schema: "facturacion",
                table: "comprobante",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "nombre_emisor",
                schema: "facturacion",
                table: "comprobante",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lugar_expedicion",
                schema: "facturacion",
                table: "comprobante");

            migrationBuilder.DropColumn(
                name: "nombre_emisor",
                schema: "facturacion",
                table: "comprobante");
        }
    }
}
