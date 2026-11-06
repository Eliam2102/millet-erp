using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P23BrechasAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "aduana_entrada",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_pago",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_cancelacion",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_reversa",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pagos_revertidos",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "revision_comprobacion",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aduana_entrada",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "fecha_pago",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "motivo_cancelacion",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "motivo_reversa",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "pagos_revertidos",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "revision_comprobacion",
                schema: "comercio_exterior",
                table: "anticipos_agente");
        }
    }
}
