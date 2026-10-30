using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorridaPagoArmarYAutorizar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "impuestos_trasladados",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "retenciones",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "subtotal",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "bruto",
                schema: "tesoreria",
                table: "corrida_pago_linea",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "proveedor_id",
                schema: "tesoreria",
                table: "corrida_pago_linea",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "retenciones",
                schema: "tesoreria",
                table: "corrida_pago_linea",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "viva",
                schema: "tesoreria",
                table: "corrida_pago_linea",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "autorizada_en",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avisos",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "firmas_recibidas",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "firmas_requeridas",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "motivo_cierre",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "rol_requerido",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "corrida_pago_firma",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    roles = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    firmada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corrida_pago_firma", x => x.id);
                    table.ForeignKey(
                        name: "fk_corrida_pago_firma_corrida_pago_corrida_id",
                        column: x => x.corrida_id,
                        principalSchema: "tesoreria",
                        principalTable: "corrida_pago",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "corrida_pago_politica_firma",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    monto_desde = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    firmas_requeridas = table.Column<int>(type: "integer", nullable: false),
                    rol_requerido = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corrida_pago_politica_firma", x => x.id);
                    table.CheckConstraint("ck_corrida_politica_firmas", "firmas_requeridas BETWEEN 1 AND 10 AND monto_desde >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_corrida_pago_linea_factura_viva",
                schema: "tesoreria",
                table: "corrida_pago_linea",
                column: "factura_proveedor_id",
                unique: true,
                filter: "viva");

            migrationBuilder.CreateIndex(
                name: "ux_corrida_pago_firma_usuario",
                schema: "tesoreria",
                table: "corrida_pago_firma",
                columns: new[] { "corrida_id", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_corrida_politica_escalon",
                schema: "tesoreria",
                table: "corrida_pago_politica_firma",
                columns: new[] { "empresa_id", "moneda", "monto_desde" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corrida_pago_firma",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "corrida_pago_politica_firma",
                schema: "tesoreria");

            migrationBuilder.DropIndex(
                name: "ux_corrida_pago_linea_factura_viva",
                schema: "tesoreria",
                table: "corrida_pago_linea");

            migrationBuilder.DropColumn(
                name: "impuestos_trasladados",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "retenciones",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "subtotal",
                schema: "tesoreria",
                table: "pasivo_pendiente_pago");

            migrationBuilder.DropColumn(
                name: "bruto",
                schema: "tesoreria",
                table: "corrida_pago_linea");

            migrationBuilder.DropColumn(
                name: "proveedor_id",
                schema: "tesoreria",
                table: "corrida_pago_linea");

            migrationBuilder.DropColumn(
                name: "retenciones",
                schema: "tesoreria",
                table: "corrida_pago_linea");

            migrationBuilder.DropColumn(
                name: "viva",
                schema: "tesoreria",
                table: "corrida_pago_linea");

            migrationBuilder.DropColumn(
                name: "autorizada_en",
                schema: "tesoreria",
                table: "corrida_pago");

            migrationBuilder.DropColumn(
                name: "avisos",
                schema: "tesoreria",
                table: "corrida_pago");

            migrationBuilder.DropColumn(
                name: "firmas_recibidas",
                schema: "tesoreria",
                table: "corrida_pago");

            migrationBuilder.DropColumn(
                name: "firmas_requeridas",
                schema: "tesoreria",
                table: "corrida_pago");

            migrationBuilder.DropColumn(
                name: "motivo_cierre",
                schema: "tesoreria",
                table: "corrida_pago");

            migrationBuilder.DropColumn(
                name: "nombre",
                schema: "tesoreria",
                table: "corrida_pago");

            migrationBuilder.DropColumn(
                name: "rol_requerido",
                schema: "tesoreria",
                table: "corrida_pago");
        }
    }
}
