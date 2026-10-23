using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ClienteUnificacionRfc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_unificacion",
                schema: "compartido",
                table: "clientes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "unificacion_con_historial",
                schema: "compartido",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "unificado_en_cliente_id",
                schema: "compartido",
                table: "clientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "unificado_en_utc",
                schema: "compartido",
                table: "clientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unificado_por",
                schema: "compartido",
                table: "clientes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_clientes_unificado_en_cliente_id",
                schema: "compartido",
                table: "clientes",
                column: "unificado_en_cliente_id");

            migrationBuilder.AddForeignKey(
                name: "fk_clientes_clientes_unificado_en_cliente_id",
                schema: "compartido",
                table: "clientes",
                column: "unificado_en_cliente_id",
                principalSchema: "compartido",
                principalTable: "clientes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_clientes_clientes_unificado_en_cliente_id",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropIndex(
                name: "ix_clientes_unificado_en_cliente_id",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "motivo_unificacion",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "unificacion_con_historial",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "unificado_en_cliente_id",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "unificado_en_utc",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "unificado_por",
                schema: "compartido",
                table: "clientes");
        }
    }
}
