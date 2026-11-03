using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerminalFacturaBanco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "factura_banco_id",
                schema: "facturacion",
                table: "terminal_liquidacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uuid_factura_banco",
                schema: "facturacion",
                table: "terminal_liquidacion",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "terminal_factura_banco",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uuid_cfdi = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    rfc_emisor = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    comision = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_comision = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cargos = table.Column<int>(type: "integer", nullable: false),
                    ligada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    ligada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminal_factura_banco", x => x.id);
                    table.CheckConstraint("ck_terminal_factura_banco_mes", "mes BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateIndex(
                name: "ix_terminal_liquidacion_factura_banco_id",
                schema: "facturacion",
                table: "terminal_liquidacion",
                column: "factura_banco_id");

            migrationBuilder.CreateIndex(
                name: "ix_terminal_liquidacion_sin_factura_fecha",
                schema: "facturacion",
                table: "terminal_liquidacion",
                columns: new[] { "empresa_id", "fecha" },
                filter: "factura_banco_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_terminal_factura_banco_empresa_cfdi",
                schema: "facturacion",
                table: "terminal_factura_banco",
                columns: new[] { "empresa_id", "cfdi_recibido_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_terminal_factura_banco_empresa_uuid",
                schema: "facturacion",
                table: "terminal_factura_banco",
                columns: new[] { "empresa_id", "uuid_cfdi" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_terminal_liquidacion_facturas_banco_terminal_factura_banco_",
                schema: "facturacion",
                table: "terminal_liquidacion",
                column: "factura_banco_id",
                principalSchema: "facturacion",
                principalTable: "terminal_factura_banco",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_terminal_liquidacion_facturas_banco_terminal_factura_banco_",
                schema: "facturacion",
                table: "terminal_liquidacion");

            migrationBuilder.DropTable(
                name: "terminal_factura_banco",
                schema: "facturacion");

            migrationBuilder.DropIndex(
                name: "ix_terminal_liquidacion_factura_banco_id",
                schema: "facturacion",
                table: "terminal_liquidacion");

            migrationBuilder.DropIndex(
                name: "ix_terminal_liquidacion_sin_factura_fecha",
                schema: "facturacion",
                table: "terminal_liquidacion");

            migrationBuilder.DropColumn(
                name: "factura_banco_id",
                schema: "facturacion",
                table: "terminal_liquidacion");

            migrationBuilder.DropColumn(
                name: "uuid_factura_banco",
                schema: "facturacion",
                table: "terminal_liquidacion");
        }
    }
}
