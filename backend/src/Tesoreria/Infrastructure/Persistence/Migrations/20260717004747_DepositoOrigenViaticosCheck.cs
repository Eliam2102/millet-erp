using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DepositoOrigenViaticosCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.AddCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                sql: "propuesta_cxc_id IS NOT NULL OR caja_sesion_id IS NOT NULL OR solicitud_viaticos_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.AddCheckConstraint(
                name: "ck_deposito_confirmacion_origen",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                sql: "propuesta_cxc_id IS NOT NULL OR caja_sesion_id IS NOT NULL");
        }
    }
}
