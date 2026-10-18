using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "info_import_agente_aduanal_naviera",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "info_import_certificado_origen",
                schema: "compras",
                table: "ordenes_compra",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "info_import_fecha_estimada_arribo",
                schema: "compras",
                table: "ordenes_compra",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "info_import_numero_bl",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "info_import_puerto_aduana_entrada",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "info_import_tipo_operacion",
                schema: "compras",
                table: "ordenes_compra",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "info_import_tipo_transporte",
                schema: "compras",
                table: "ordenes_compra",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fraccion_arancelaria",
                schema: "compras",
                table: "orden_compra_lineas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "info_import_agente_aduanal_naviera",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "info_import_certificado_origen",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "info_import_fecha_estimada_arribo",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "info_import_numero_bl",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "info_import_puerto_aduana_entrada",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "info_import_tipo_operacion",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "info_import_tipo_transporte",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "fraccion_arancelaria",
                schema: "compras",
                table: "orden_compra_lineas");
        }
    }
}
