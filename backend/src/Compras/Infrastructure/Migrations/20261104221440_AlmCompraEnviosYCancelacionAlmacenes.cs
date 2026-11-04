using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlmCompraEnviosYCancelacionAlmacenes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_alm_compra_envios_estado",
                schema: "compras",
                table: "alm_compra_envios");

            migrationBuilder.AddColumn<bool>(
                name: "espera_almacenes",
                schema: "compras",
                table: "oc_solicitudes_cancelacion",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "respuesta_almacenes",
                schema: "compras",
                table: "oc_solicitudes_cancelacion",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_alm_compra_envios_estado",
                schema: "compras",
                table: "alm_compra_envios",
                sql: "estado IN ('PENDIENTE','ENVIADO','PROCESADO','DUPLICADO','ERROR','RECHAZADO')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_alm_compra_envios_estado",
                schema: "compras",
                table: "alm_compra_envios");

            migrationBuilder.DropColumn(
                name: "espera_almacenes",
                schema: "compras",
                table: "oc_solicitudes_cancelacion");

            migrationBuilder.DropColumn(
                name: "respuesta_almacenes",
                schema: "compras",
                table: "oc_solicitudes_cancelacion");

            migrationBuilder.AddCheckConstraint(
                name: "ck_alm_compra_envios_estado",
                schema: "compras",
                table: "alm_compra_envios",
                sql: "estado IN ('PENDIENTE','ENVIADO','PROCESADO','DUPLICADO','ERROR')");
        }
    }
}
