using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DiferenciaCorte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "diferencia_corte",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_sesion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cajero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia_corte = table.Column<DateOnly>(type: "date", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    motivo_rechazo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    importe_devuelto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    resuelta_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diferencia_corte", x => x.id);
                    table.CheckConstraint("ck_diferencia_corte_estado", "estado IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_diferencia_corte_importe", "importe <> 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_diferencia_corte_empresa_estado",
                schema: "tesoreria",
                table: "diferencia_corte",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_diferencia_corte_caja_sesion",
                schema: "tesoreria",
                table: "diferencia_corte",
                column: "caja_sesion_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diferencia_corte",
                schema: "tesoreria");
        }
    }
}
