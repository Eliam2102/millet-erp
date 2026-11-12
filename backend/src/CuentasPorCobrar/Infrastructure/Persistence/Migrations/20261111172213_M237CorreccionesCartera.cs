using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M237CorreccionesCartera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "plazo_corregido_dias",
                schema: "cuentas_por_cobrar",
                table: "factura_cartera",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "correccion_cartera",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_cartera_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    motivo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    solicitada_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitada_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    solicitada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decidida_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decidida_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decidida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_decision = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    plazo_anterior_dias = table.Column<int>(type: "integer", nullable: true),
                    plazo_nuevo_dias = table.Column<int>(type: "integer", nullable: true),
                    vencimiento_anterior = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    vencimiento_nuevo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    movimiento_cartera_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movimiento_tipo = table.Column<short>(type: "smallint", nullable: true),
                    movimiento_importe = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correccion_cartera", x => x.id);
                    table.CheckConstraint("ck_correccion_cartera_estado", "estado IN (1, 2, 3)");
                    table.CheckConstraint("ck_correccion_cartera_tipo", "tipo IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_correccion_cartera_factura_cartera_factura_cartera_id",
                        column: x => x.factura_cartera_id,
                        principalSchema: "cuentas_por_cobrar",
                        principalTable: "factura_cartera",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_correccion_cartera_factura",
                schema: "cuentas_por_cobrar",
                table: "correccion_cartera",
                column: "factura_cartera_id");

            migrationBuilder.CreateIndex(
                name: "ix_correccion_cartera_tipo_estado",
                schema: "cuentas_por_cobrar",
                table: "correccion_cartera",
                columns: new[] { "tipo", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_correccion_cartera_movimiento_pendiente",
                schema: "cuentas_por_cobrar",
                table: "correccion_cartera",
                column: "movimiento_cartera_id",
                unique: true,
                filter: "estado = 1 AND movimiento_cartera_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correccion_cartera",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropColumn(
                name: "plazo_corregido_dias",
                schema: "cuentas_por_cobrar",
                table: "factura_cartera");
        }
    }
}
