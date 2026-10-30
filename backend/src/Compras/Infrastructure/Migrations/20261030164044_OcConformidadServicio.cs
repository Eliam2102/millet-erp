using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OcConformidadServicio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "servicio_confirmacion_revocada_en",
                schema: "compras",
                table: "ordenes_compra",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "servicio_confirmacion_revocada_motivo",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "servicio_confirmacion_revocada_por_id",
                schema: "compras",
                table: "ordenes_compra",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "servicio_confirmado_comentario",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "servicio_confirmado_en",
                schema: "compras",
                table: "ordenes_compra",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "servicio_confirmado_por_id",
                schema: "compras",
                table: "ordenes_compra",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "servicio_no_conforme_en",
                schema: "compras",
                table: "ordenes_compra",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "servicio_no_conforme_motivo",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "servicio_no_conforme_por_id",
                schema: "compras",
                table: "ordenes_compra",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "servicio_confirmacion_revocada_en",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "servicio_confirmacion_revocada_motivo",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "servicio_confirmacion_revocada_por_id",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "servicio_confirmado_comentario",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "servicio_confirmado_en",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "servicio_confirmado_por_id",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "servicio_no_conforme_en",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "servicio_no_conforme_motivo",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "servicio_no_conforme_por_id",
                schema: "compras",
                table: "ordenes_compra");
        }
    }
}
