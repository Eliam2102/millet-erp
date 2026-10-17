using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotaCreditoReembolso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "importe_reembolso_solicitado",
                schema: "facturacion",
                table: "nota_credito",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reembolso_solicitado_en",
                schema: "facturacion",
                table: "nota_credito",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "importe_reembolso_solicitado",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_solicitado_en",
                schema: "facturacion",
                table: "nota_credito");
        }
    }
}
