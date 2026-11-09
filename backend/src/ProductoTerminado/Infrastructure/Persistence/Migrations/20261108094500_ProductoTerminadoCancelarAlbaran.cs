using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductoTerminadoCancelarAlbaran : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelado_at",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelado_por_id",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "entregado_at",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "liquidado_at",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_cancelacion",
                schema: "producto_terminado",
                table: "albaranes_pt",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancelado_at",
                schema: "producto_terminado",
                table: "albaranes_pt");

            migrationBuilder.DropColumn(
                name: "cancelado_por_id",
                schema: "producto_terminado",
                table: "albaranes_pt");

            migrationBuilder.DropColumn(
                name: "entregado_at",
                schema: "producto_terminado",
                table: "albaranes_pt");

            migrationBuilder.DropColumn(
                name: "liquidado_at",
                schema: "producto_terminado",
                table: "albaranes_pt");

            migrationBuilder.DropColumn(
                name: "motivo_cancelacion",
                schema: "producto_terminado",
                table: "albaranes_pt");
        }
    }
}
