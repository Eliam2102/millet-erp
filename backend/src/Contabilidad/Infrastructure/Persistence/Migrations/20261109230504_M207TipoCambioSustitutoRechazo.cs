using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M207TipoCambioSustitutoRechazo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "tc_accion",
                schema: "contabilidad",
                table: "rechazos_contables",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tc_autorizado_en",
                schema: "contabilidad",
                table: "rechazos_contables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tc_autorizado_por",
                schema: "contabilidad",
                table: "rechazos_contables",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tc_capturado_en",
                schema: "contabilidad",
                table: "rechazos_contables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tc_capturado_por",
                schema: "contabilidad",
                table: "rechazos_contables",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tc_capturado_por_id",
                schema: "contabilidad",
                table: "rechazos_contables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tc_motivo",
                schema: "contabilidad",
                table: "rechazos_contables",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tc_valor",
                schema: "contabilidad",
                table: "rechazos_contables",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tc_accion",
                schema: "contabilidad",
                table: "rechazos_contables");

            migrationBuilder.DropColumn(
                name: "tc_autorizado_en",
                schema: "contabilidad",
                table: "rechazos_contables");

            migrationBuilder.DropColumn(
                name: "tc_autorizado_por",
                schema: "contabilidad",
                table: "rechazos_contables");

            migrationBuilder.DropColumn(
                name: "tc_capturado_en",
                schema: "contabilidad",
                table: "rechazos_contables");

            migrationBuilder.DropColumn(
                name: "tc_capturado_por",
                schema: "contabilidad",
                table: "rechazos_contables");

            migrationBuilder.DropColumn(
                name: "tc_capturado_por_id",
                schema: "contabilidad",
                table: "rechazos_contables");

            migrationBuilder.DropColumn(
                name: "tc_motivo",
                schema: "contabilidad",
                table: "rechazos_contables");

            migrationBuilder.DropColumn(
                name: "tc_valor",
                schema: "contabilidad",
                table: "rechazos_contables");
        }
    }
}
