using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P17RechazoVentaActivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_rechazo",
                schema: "facturacion",
                table: "autorizacion_venta_activo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rechazado_en",
                schema: "facturacion",
                table: "autorizacion_venta_activo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rechazado_por",
                schema: "facturacion",
                table: "autorizacion_venta_activo",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "motivo_rechazo",
                schema: "facturacion",
                table: "autorizacion_venta_activo");

            migrationBuilder.DropColumn(
                name: "rechazado_en",
                schema: "facturacion",
                table: "autorizacion_venta_activo");

            migrationBuilder.DropColumn(
                name: "rechazado_por",
                schema: "facturacion",
                table: "autorizacion_venta_activo");
        }
    }
}
