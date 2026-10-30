using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotaCreditoOrigenAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "autorizada_en",
                schema: "facturacion",
                table: "nota_credito",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "autorizada_por_id",
                schema: "facturacion",
                table: "nota_credito",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "aw_numero_nota",
                schema: "facturacion",
                table: "nota_credito",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "aw_numero_pedido",
                schema: "facturacion",
                table: "nota_credito",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "aw_version_nota",
                schema: "facturacion",
                table: "nota_credito",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "aw_version_pedido",
                schema: "facturacion",
                table: "nota_credito",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_nota_credito_aw_numero_nota",
                schema: "facturacion",
                table: "nota_credito",
                column: "aw_numero_nota",
                unique: true,
                filter: "aw_numero_nota IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_nota_credito_aw_numero_nota",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "autorizada_en",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "autorizada_por_id",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "aw_numero_nota",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "aw_numero_pedido",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "aw_version_nota",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "aw_version_pedido",
                schema: "facturacion",
                table: "nota_credito");
        }
    }
}
