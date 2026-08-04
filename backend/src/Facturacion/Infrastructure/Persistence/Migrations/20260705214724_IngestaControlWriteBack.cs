using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IngestaControlWriteBack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "write_back_at",
                schema: "facturacion",
                table: "ingesta_control",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "write_back_estado",
                schema: "facturacion",
                table: "ingesta_control",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "write_back_intentos",
                schema: "facturacion",
                table: "ingesta_control",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "write_back_motivo",
                schema: "facturacion",
                table: "ingesta_control",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "write_back_pendiente",
                schema: "facturacion",
                table: "ingesta_control",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "write_back_resultado",
                schema: "facturacion",
                table: "ingesta_control",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "write_back_solicitud_id",
                schema: "facturacion",
                table: "ingesta_control",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "write_back_ultimo_error",
                schema: "facturacion",
                table: "ingesta_control",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "write_back_uuid",
                schema: "facturacion",
                table: "ingesta_control",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_ingesta_control_write_back_pendiente",
                schema: "facturacion",
                table: "ingesta_control",
                column: "write_back_pendiente",
                filter: "write_back_pendiente");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ingesta_control_write_back_pendiente",
                schema: "facturacion",
                table: "ingesta_control");

            migrationBuilder.DropColumn(
                name: "write_back_at",
                schema: "facturacion",
                table: "ingesta_control");

            migrationBuilder.DropColumn(
                name: "write_back_estado",
                schema: "facturacion",
                table: "ingesta_control");

            migrationBuilder.DropColumn(
                name: "write_back_intentos",
                schema: "facturacion",
                table: "ingesta_control");

            migrationBuilder.DropColumn(
                name: "write_back_motivo",
                schema: "facturacion",
                table: "ingesta_control");

            migrationBuilder.DropColumn(
                name: "write_back_pendiente",
                schema: "facturacion",
                table: "ingesta_control");

            migrationBuilder.DropColumn(
                name: "write_back_resultado",
                schema: "facturacion",
                table: "ingesta_control");

            migrationBuilder.DropColumn(
                name: "write_back_solicitud_id",
                schema: "facturacion",
                table: "ingesta_control");

            migrationBuilder.DropColumn(
                name: "write_back_ultimo_error",
                schema: "facturacion",
                table: "ingesta_control");

            migrationBuilder.DropColumn(
                name: "write_back_uuid",
                schema: "facturacion",
                table: "ingesta_control");
        }
    }
}
