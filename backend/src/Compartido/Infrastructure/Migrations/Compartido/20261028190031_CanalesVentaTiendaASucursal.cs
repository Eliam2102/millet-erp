using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class CanalesVentaTiendaASucursal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "canales_venta",
                keyColumn: "id",
                keyValue: (short)1,
                column: "nombre",
                value: "Sucursal Cancún");

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "canales_venta",
                keyColumn: "id",
                keyValue: (short)2,
                column: "nombre",
                value: "Sucursal Circuito");

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "canales_venta",
                keyColumn: "id",
                keyValue: (short)3,
                column: "nombre",
                value: "Sucursal Chichí Suárez");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "canales_venta",
                keyColumn: "id",
                keyValue: (short)1,
                column: "nombre",
                value: "Tienda Cancún");

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "canales_venta",
                keyColumn: "id",
                keyValue: (short)2,
                column: "nombre",
                value: "Tienda Circuito");

            migrationBuilder.UpdateData(
                schema: "compartido",
                table: "canales_venta",
                keyColumn: "id",
                keyValue: (short)3,
                column: "nombre",
                value: "Tienda Chichí Suárez");
        }
    }
}
