using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MotivoCancelacionIdEnOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "motivo_cancelacion_id",
                schema: "compras",
                table: "ordenes_compra",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "motivo_cancelacion_id",
                schema: "compras",
                table: "ordenes_compra");
        }
    }
}
