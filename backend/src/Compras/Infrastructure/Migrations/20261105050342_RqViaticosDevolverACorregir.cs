using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RqViaticosDevolverACorregir : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "comentario_devolucion",
                schema: "compras",
                table: "requisiciones",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "devuelta_por_id",
                schema: "compras",
                table: "requisiciones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_devolucion",
                schema: "compras",
                table: "requisiciones",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "comentario_devolucion",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropColumn(
                name: "devuelta_por_id",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropColumn(
                name: "fecha_devolucion",
                schema: "compras",
                table: "requisiciones");
        }
    }
}
