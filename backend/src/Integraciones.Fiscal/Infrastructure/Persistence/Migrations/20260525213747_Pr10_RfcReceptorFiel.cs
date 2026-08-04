using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pr10_RfcReceptorFiel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cer_file_id_externo",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fiel_subida_at",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fiel_valid_from",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fiel_valid_to",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "key_file_id_externo",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "person_id_externo",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cer_file_id_externo",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores");

            migrationBuilder.DropColumn(
                name: "fiel_subida_at",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores");

            migrationBuilder.DropColumn(
                name: "fiel_valid_from",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores");

            migrationBuilder.DropColumn(
                name: "fiel_valid_to",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores");

            migrationBuilder.DropColumn(
                name: "key_file_id_externo",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores");

            migrationBuilder.DropColumn(
                name: "person_id_externo",
                schema: "integraciones_fiscal",
                table: "rfcs_receptores");
        }
    }
}
