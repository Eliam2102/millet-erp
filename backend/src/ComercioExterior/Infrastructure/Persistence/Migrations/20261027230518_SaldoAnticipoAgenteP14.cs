using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SaldoAnticipoAgenteP14 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "anticipo_agente_id",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "cuenta_peca",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "gastos_aplicados_mxn",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ix_gastos_expediente_anticipo_agente_id",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                column: "anticipo_agente_id");

            migrationBuilder.AddForeignKey(
                name: "fk_gastos_expediente_anticipos_agente_anticipo_agente_id",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                column: "anticipo_agente_id",
                principalSchema: "comercio_exterior",
                principalTable: "anticipos_agente",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_gastos_expediente_anticipos_agente_anticipo_agente_id",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropIndex(
                name: "ix_gastos_expediente_anticipo_agente_id",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "anticipo_agente_id",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "cuenta_peca",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "gastos_aplicados_mxn",
                schema: "comercio_exterior",
                table: "anticipos_agente");
        }
    }
}
