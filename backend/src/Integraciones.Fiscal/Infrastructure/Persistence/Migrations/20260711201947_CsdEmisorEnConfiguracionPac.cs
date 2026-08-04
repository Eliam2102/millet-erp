using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CsdEmisorEnConfiguracionPac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "csd_actualizado_at",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "csd_certificado_cifrado",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "csd_hash",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "csd_llave_privada_cifrada",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "csd_password_cifrado",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "csd_actualizado_at",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "csd_certificado_cifrado",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "csd_hash",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "csd_llave_privada_cifrada",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");

            migrationBuilder.DropColumn(
                name: "csd_password_cifrado",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");
        }
    }
}
