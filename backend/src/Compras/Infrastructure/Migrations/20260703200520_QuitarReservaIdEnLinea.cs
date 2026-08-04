using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuitarReservaIdEnLinea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reserva_id",
                schema: "compras",
                table: "requisicion_lineas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reserva_id",
                schema: "compras",
                table: "requisicion_lineas",
                type: "uuid",
                nullable: true);
        }
    }
}
