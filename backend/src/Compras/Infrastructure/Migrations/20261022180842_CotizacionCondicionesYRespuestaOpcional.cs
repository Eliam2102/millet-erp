using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CotizacionCondicionesYRespuestaOpcional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<short>(
                name: "tipo",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                type: "smallint",
                nullable: true,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "fecha_respuesta",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<string>(
                name: "evidencia",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AddColumn<Guid>(
                name: "condiciones_pago_id",
                schema: "compras",
                table: "cotizaciones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_entrega",
                schema: "compras",
                table: "cotizaciones",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "condiciones_pago_id",
                schema: "compras",
                table: "cotizaciones");

            migrationBuilder.DropColumn(
                name: "fecha_entrega",
                schema: "compras",
                table: "cotizaciones");

            migrationBuilder.AlterColumn<short>(
                name: "tipo",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "fecha_respuesta",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "evidencia",
                schema: "compras",
                table: "oc_respuestas_proveedor",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);
        }
    }
}
