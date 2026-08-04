using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PasivosInternosBeneficiario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "beneficiario_id",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "origen_id",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "origen_tipo",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tipo_beneficiario",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Backfill: todas las filas existentes son pasivos de factura.
            migrationBuilder.Sql("""
                UPDATE tesoreria.pasivo_pendiente_pago
                SET tipo_beneficiario = 'Proveedor',
                    origen_tipo = 'Factura',
                    origen_id = factura_proveedor_id,
                    beneficiario_id = proveedor_id
                WHERE tipo_beneficiario = '';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_pasivo_tipo_beneficiario",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                column: "tipo_beneficiario");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pasivo_tipo_beneficiario",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "beneficiario_id",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "origen_id",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "origen_tipo",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "tipo_beneficiario",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");
        }
    }
}
