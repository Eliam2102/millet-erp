using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DepositoFichasCorteCaja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_deposito_confirmacion_estado",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.AddColumn<Guid>(
                name: "cajero_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "dia_corte",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sucursal_id",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "deposito_ficha",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deposito_confirmacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fecha_ficha = table.Column<DateOnly>(type: "date", nullable: false),
                    numero_ficha = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    soporte_tipo = table.Column<short>(type: "smallint", nullable: true),
                    soporte_folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    confirmada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposito_ficha", x => x.id);
                    table.CheckConstraint("ck_deposito_ficha_importe", "importe > 0");
                    table.CheckConstraint("ck_deposito_ficha_soporte_tipo", "soporte_tipo IS NULL OR soporte_tipo IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_deposito_ficha_deposito_confirmacion_deposito_confirmacion_",
                        column: x => x.deposito_confirmacion_id,
                        principalSchema: "tesoreria",
                        principalTable: "deposito_confirmacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_deposito_ficha_movimiento_bancario_movimiento_id",
                        column: x => x.movimiento_id,
                        principalSchema: "tesoreria",
                        principalTable: "movimiento_bancario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_deposito_confirmacion_estado",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                sql: "estado IN (1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "ix_deposito_ficha_deposito",
                schema: "tesoreria",
                table: "deposito_ficha",
                column: "deposito_confirmacion_id");

            migrationBuilder.CreateIndex(
                name: "ux_deposito_ficha_movimiento",
                schema: "tesoreria",
                table: "deposito_ficha",
                column: "movimiento_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deposito_ficha",
                schema: "tesoreria");

            migrationBuilder.DropCheckConstraint(
                name: "ck_deposito_confirmacion_estado",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "cajero_id",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "dia_corte",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "sucursal_id",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.AddCheckConstraint(
                name: "ck_deposito_confirmacion_estado",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                sql: "estado IN (1, 2, 3)");
        }
    }
}
