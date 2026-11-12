using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M238CorreccionLiberacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "correccion_liberacion",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_ref = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    decision_retenida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision_reevaluada_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    detalle = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    corregida_por = table.Column<Guid>(type: "uuid", nullable: false),
                    corregida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correccion_liberacion", x => x.id);
                    table.CheckConstraint("ck_correccion_liberacion_distintas", "decision_retenida_id != decision_reevaluada_id");
                    table.CheckConstraint("ck_correccion_liberacion_tipo", "tipo IN (1, 2, 3)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_correccion_liberacion_pedido",
                schema: "cuentas_por_cobrar",
                table: "correccion_liberacion",
                column: "pedido_ref");

            migrationBuilder.CreateIndex(
                name: "ix_correccion_liberacion_reevaluada",
                schema: "cuentas_por_cobrar",
                table: "correccion_liberacion",
                column: "decision_reevaluada_id");

            migrationBuilder.CreateIndex(
                name: "ux_correccion_liberacion_retenida",
                schema: "cuentas_por_cobrar",
                table: "correccion_liberacion",
                column: "decision_retenida_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correccion_liberacion",
                schema: "cuentas_por_cobrar");
        }
    }
}
