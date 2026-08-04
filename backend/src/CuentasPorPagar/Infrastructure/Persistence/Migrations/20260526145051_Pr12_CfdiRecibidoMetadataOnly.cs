using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pr12_CfdiRecibidoMetadataOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "xml_hash_sha256",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "xml_blob_ref",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(400)",
                oldMaxLength: 400);

            migrationBuilder.AddColumn<string>(
                name: "request_id_externo_fiscal_api",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "solicitud_descarga_id",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "request_id_externo_fiscal_api",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos");

            migrationBuilder.DropColumn(
                name: "solicitud_descarga_id",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos");

            migrationBuilder.AlterColumn<string>(
                name: "xml_hash_sha256",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "xml_blob_ref",
                schema: "cuentas_por_pagar",
                table: "cfdis_recibidos",
                type: "character varying(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(400)",
                oldMaxLength: 400,
                oldNullable: true);
        }
    }
}
