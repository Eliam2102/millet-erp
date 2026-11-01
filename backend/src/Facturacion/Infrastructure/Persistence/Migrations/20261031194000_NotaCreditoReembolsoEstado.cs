using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// F6.3 (86e3kdypx): reembolso programado al emitir la NC (cuenta destino) y el estado que reporta Tesorería. Las NC que
    /// ya habían pedido reembolso quedan como «Solicitado».
    /// </summary>
    public partial class NotaCreditoReembolsoEstado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "estado_reembolso",
                schema: "facturacion",
                table: "nota_credito",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset?>(
                name: "reembolso_actualizado_en",
                schema: "facturacion",
                table: "nota_credito",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "reembolso_al_timbrar",
                schema: "facturacion",
                table: "nota_credito",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reembolso_banco",
                schema: "facturacion",
                table: "nota_credito",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reembolso_clabe",
                schema: "facturacion",
                table: "nota_credito",
                type: "character varying(18)",
                maxLength: 18,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reembolso_motivo_resolucion",
                schema: "facturacion",
                table: "nota_credito",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly?>(
                name: "reembolso_pagado_el",
                schema: "facturacion",
                table: "nota_credito",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reembolso_referencia",
                schema: "facturacion",
                table: "nota_credito",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reembolso_titular",
                schema: "facturacion",
                table: "nota_credito",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE facturacion.nota_credito SET estado_reembolso = 'Solicitado' WHERE reembolso_solicitado_en IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reembolso_titular",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_referencia",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_pagado_el",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_motivo_resolucion",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_clabe",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_banco",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_al_timbrar",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "reembolso_actualizado_en",
                schema: "facturacion",
                table: "nota_credito");

            migrationBuilder.DropColumn(
                name: "estado_reembolso",
                schema: "facturacion",
                table: "nota_credito");
        }
    }
}
