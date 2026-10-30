using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BloqueoProveedor86e3tactf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "bloqueado",
                schema: "compartido",
                table: "proveedores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bloqueado_en",
                schema: "compartido",
                table: "proveedores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "bloqueado_por_id",
                schema: "compartido",
                table: "proveedores",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_bloqueo",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bloqueado",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "bloqueado_en",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "bloqueado_por_id",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "motivo_bloqueo",
                schema: "compartido",
                table: "proveedores");
        }
    }
}
