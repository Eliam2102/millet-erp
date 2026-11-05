using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductoTerminadoEntrega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "entregado_at",
                schema: "producto_terminado",
                table: "pedidos_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "estado115at",
                schema: "producto_terminado",
                table: "pedidos_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "facturado_at",
                schema: "producto_terminado",
                table: "pedidos_pt",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "entregas_pt",
                schema: "producto_terminado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salida_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    albaran_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_pt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    receptor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entregadas = table.Column<int>(type: "integer", nullable: false),
                    rechazadas = table.Column<int>(type: "integer", nullable: false),
                    motivo_rechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    firma = table.Column<byte[]>(type: "bytea", nullable: true),
                    foto = table.Column<byte[]>(type: "bytea", nullable: true),
                    cobro_monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cobro_forma = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cobro_referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cobro_conciliado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cobro_mostrador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entregada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entregas_pt", x => x.id);
                    table.ForeignKey(
                        name: "fk_entregas_pt_albaranes_pt_albaran_pt_id",
                        column: x => x.albaran_pt_id,
                        principalSchema: "producto_terminado",
                        principalTable: "albaranes_pt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entregas_pt_albaran_pt_id",
                schema: "producto_terminado",
                table: "entregas_pt",
                column: "albaran_pt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entregas_pt_pedido_pt_id",
                schema: "producto_terminado",
                table: "entregas_pt",
                column: "pedido_pt_id");

            migrationBuilder.CreateIndex(
                name: "ix_pt_entregas_cobro_pendiente",
                schema: "producto_terminado",
                table: "entregas_pt",
                column: "cobro_conciliado_at",
                filter: "cobro_monto IS NOT NULL AND cobro_conciliado_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entregas_pt",
                schema: "producto_terminado");

            migrationBuilder.DropColumn(
                name: "entregado_at",
                schema: "producto_terminado",
                table: "pedidos_pt");

            migrationBuilder.DropColumn(
                name: "estado115at",
                schema: "producto_terminado",
                table: "pedidos_pt");

            migrationBuilder.DropColumn(
                name: "facturado_at",
                schema: "producto_terminado",
                table: "pedidos_pt");
        }
    }
}
