using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P19AnticiposAgente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.AddColumn<Guid>(
                name: "sucursal_anticipo_ce_id",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "agente_ce_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "anticipo_agente_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "devolucion_ce_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_deposito_confirmacion_devolucion_ce_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                column: "devolucion_ce_id",
                unique: true,
                filter: "devolucion_ce_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                sql: "propuesta_cxc_id IS NOT NULL OR caja_sesion_id IS NOT NULL OR solicitud_viaticos_id IS NOT NULL OR devolucion_ce_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deposito_confirmacion_devolucion_ce_id",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "sucursal_anticipo_ce_id",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "agente_ce_id",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "anticipo_agente_id",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "devolucion_ce_id",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.AddCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                sql: "propuesta_cxc_id IS NOT NULL OR caja_sesion_id IS NOT NULL OR solicitud_viaticos_id IS NOT NULL");
        }
    }
}
