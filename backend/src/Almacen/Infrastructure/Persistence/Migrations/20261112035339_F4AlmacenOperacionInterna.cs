using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F4AlmacenOperacionInterna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "evidencia_blob_ref",
                schema: "almacen",
                table: "salidas_rotura",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decidido_at",
                schema: "almacen",
                table: "mensajes_app",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decidido_por",
                schema: "almacen",
                table: "mensajes_app",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_rechazo",
                schema: "almacen",
                table: "mensajes_app",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_aplicada",
                schema: "almacen",
                table: "lineas_conteo",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "folio_ajuste",
                schema: "almacen",
                table: "lineas_conteo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "saldo_resultante",
                schema: "almacen",
                table: "lineas_conteo",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "evidencia_blob_ref",
                schema: "almacen",
                table: "salidas_rotura");

            migrationBuilder.DropColumn(
                name: "decidido_at",
                schema: "almacen",
                table: "mensajes_app");

            migrationBuilder.DropColumn(
                name: "decidido_por",
                schema: "almacen",
                table: "mensajes_app");

            migrationBuilder.DropColumn(
                name: "motivo_rechazo",
                schema: "almacen",
                table: "mensajes_app");

            migrationBuilder.DropColumn(
                name: "cantidad_aplicada",
                schema: "almacen",
                table: "lineas_conteo");

            migrationBuilder.DropColumn(
                name: "folio_ajuste",
                schema: "almacen",
                table: "lineas_conteo");

            migrationBuilder.DropColumn(
                name: "saldo_resultante",
                schema: "almacen",
                table: "lineas_conteo");
        }
    }
}
