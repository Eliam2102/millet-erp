using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DevolucionClienteEditadaPor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "editada_en",
                schema: "tesoreria",
                table: "devolucion_cliente",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "editada_por",
                schema: "tesoreria",
                table: "devolucion_cliente",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "editada_en",
                schema: "tesoreria",
                table: "devolucion_cliente");

            migrationBuilder.DropColumn(
                name: "editada_por",
                schema: "tesoreria",
                table: "devolucion_cliente");
        }
    }
}
