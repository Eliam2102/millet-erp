using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RespuestaAnuladaYAcuseEnvioOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anulada_en",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "anulada_por_id",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_anulacion",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "acuse_fecha",
                schema: "compras",
                table: "envios_oc",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acuse_referencia",
                schema: "compras",
                table: "envios_oc",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acuse_usuario_nombre",
                schema: "compras",
                table: "envios_oc",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "anulada_en",
                schema: "compras",
                table: "oc_respuestas_proveedor");

            migrationBuilder.DropColumn(
                name: "anulada_por_id",
                schema: "compras",
                table: "oc_respuestas_proveedor");

            migrationBuilder.DropColumn(
                name: "motivo_anulacion",
                schema: "compras",
                table: "oc_respuestas_proveedor");

            migrationBuilder.DropColumn(
                name: "acuse_fecha",
                schema: "compras",
                table: "envios_oc");

            migrationBuilder.DropColumn(
                name: "acuse_referencia",
                schema: "compras",
                table: "envios_oc");

            migrationBuilder.DropColumn(
                name: "acuse_usuario_nombre",
                schema: "compras",
                table: "envios_oc");
        }
    }
}
