using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CancelacionDosPasos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decidida_en",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decidida_por_id",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decidida_por_nombre",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_rechazo",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "solicitada_por_id",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "solicitada_por_nombre",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "decidida_en",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "decidida_por_id",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "decidida_por_nombre",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "motivo_rechazo",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "solicitada_por_id",
                schema: "facturacion",
                table: "solicitud_cancelacion");

            migrationBuilder.DropColumn(
                name: "solicitada_por_nombre",
                schema: "facturacion",
                table: "solicitud_cancelacion");
        }
    }
}
