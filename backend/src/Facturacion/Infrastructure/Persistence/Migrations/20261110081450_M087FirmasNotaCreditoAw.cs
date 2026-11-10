using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M087FirmasNotaCreditoAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "firma_credito_en",
                schema: "facturacion",
                table: "nota_credito",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "firma_credito_por_id",
                schema: "facturacion",
                table: "nota_credito",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "firma_gerente_en",
                schema: "facturacion",
                table: "nota_credito",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "firma_gerente_por_id",
                schema: "facturacion",
                table: "nota_credito",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "firma_credito_en",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "firma_credito_por_id",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "firma_gerente_en",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "firma_gerente_por_id",
                schema: "facturacion",
                table: "nota_credito");
        }
    }
}
