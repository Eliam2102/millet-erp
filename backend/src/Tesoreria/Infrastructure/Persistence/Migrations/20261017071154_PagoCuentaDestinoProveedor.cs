using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PagoCuentaDestinoProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "destino_banco",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "destino_cuenta_id",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destino_numero_cuenta",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destino_titular",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "fondos_verificados",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "destino_banco",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropColumn(
                name: "destino_cuenta_id",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropColumn(
                name: "destino_numero_cuenta",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropColumn(
                name: "destino_titular",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropColumn(
                name: "fondos_verificados",
                schema: "tesoreria",
                table: "movimiento_bancario");
        }
    }
}
