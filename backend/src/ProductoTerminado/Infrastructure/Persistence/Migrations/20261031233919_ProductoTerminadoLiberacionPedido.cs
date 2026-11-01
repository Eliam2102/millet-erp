using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductoTerminadoLiberacionPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "liberacion",
                schema: "producto_terminado",
                table: "pedidos_pt",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "liberacion_at",
                schema: "producto_terminado",
                table: "pedidos_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_liberacion",
                schema: "producto_terminado",
                table: "pedidos_pt",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "saldos_demo",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_pedido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    saldo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saldos_demo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pt_pedidos_retenidos",
                schema: "producto_terminado",
                table: "pedidos_pt",
                column: "liberacion",
                filter: "liberacion = 'Retenido'");

            migrationBuilder.CreateIndex(
                name: "ix_saldos_demo_empresa_id_numero_pedido",
                schema: "producto_terminado",
                table: "saldos_demo",
                columns: new[] { "empresa_id", "numero_pedido" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saldos_demo",
                schema: "producto_terminado");

            migrationBuilder.DropIndex(
                name: "ix_pt_pedidos_retenidos",
                schema: "producto_terminado",
                table: "pedidos_pt");

            migrationBuilder.DropColumn(
                name: "liberacion",
                schema: "producto_terminado",
                table: "pedidos_pt");

            migrationBuilder.DropColumn(
                name: "liberacion_at",
                schema: "producto_terminado",
                table: "pedidos_pt");

            migrationBuilder.DropColumn(
                name: "motivo_liberacion",
                schema: "producto_terminado",
                table: "pedidos_pt");
        }
    }
}
