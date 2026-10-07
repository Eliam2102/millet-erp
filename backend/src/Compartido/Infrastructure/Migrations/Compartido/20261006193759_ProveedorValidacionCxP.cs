using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProveedorValidacionCxP : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_rechazo",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "validado_en",
                schema: "compartido",
                table: "proveedores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "validado_por_id",
                schema: "compartido",
                table: "proveedores",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "motivo_rechazo",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "validado_en",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "validado_por_id",
                schema: "compartido",
                table: "proveedores");
        }
    }
}
