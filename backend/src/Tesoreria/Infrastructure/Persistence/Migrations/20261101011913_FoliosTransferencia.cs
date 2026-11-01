using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FoliosTransferencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "folio_secuencias_transferencia",
                schema: "tesoreria",
                columns: table => new
                {
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    siguiente = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folio_secuencias_transferencia", x => x.empresa_id);
                });

            migrationBuilder.CreateTable(
                name: "folio_transferencia",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    emitido_por = table.Column<Guid>(type: "uuid", nullable: false),
                    emitido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cobro_mostrador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    cancelado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_folio_transferencia", x => x.id);
                    table.CheckConstraint("ck_folio_transferencia_estado", "estado IN (1, 2, 3)");
                    table.CheckConstraint("ck_folio_transferencia_importe", "importe > 0");
                    table.CheckConstraint("ck_folio_transferencia_usado", "(estado = 2) = (cobro_mostrador_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_folio_transferencia_movimiento_bancario_movimiento_id",
                        column: x => x.movimiento_id,
                        principalSchema: "tesoreria",
                        principalTable: "movimiento_bancario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_folio_transferencia_cliente_estado",
                schema: "tesoreria",
                table: "folio_transferencia",
                columns: new[] { "cliente_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_folio_transferencia_movimiento",
                schema: "tesoreria",
                table: "folio_transferencia",
                column: "movimiento_id");

            migrationBuilder.CreateIndex(
                name: "ux_folio_transferencia_empresa_folio",
                schema: "tesoreria",
                table: "folio_transferencia",
                columns: new[] { "empresa_id", "folio" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "folio_secuencias_transferencia",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "folio_transferencia",
                schema: "tesoreria");
        }
    }
}
