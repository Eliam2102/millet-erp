using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DepositoViaticosEsperado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "solicitud_viaticos_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_deposito_confirmacion_solicitud_viaticos",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                column: "solicitud_viaticos_id",
                unique: true,
                filter: "solicitud_viaticos_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_deposito_confirmacion_solicitud_viaticos",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "solicitud_viaticos_id",
                schema: "tesoreria",
                table: "deposito_confirmacion");
        }
    }
}
