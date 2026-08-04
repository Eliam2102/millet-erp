using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Cobranza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seguimiento_cobranza",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<short>(type: "smallint", nullable: false),
                    resultado = table.Column<short>(type: "smallint", nullable: false),
                    monto_comprometido = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    fecha_comprometida = table.Column<DateOnly>(type: "date", nullable: true),
                    nota = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seguimiento_cobranza", x => x.id);
                    table.CheckConstraint("ck_seguimiento_cobranza_canal", "canal IN (1, 2, 3)");
                    table.CheckConstraint("ck_seguimiento_cobranza_promesa_consistente", "(resultado = 1 AND monto_comprometido IS NOT NULL AND monto_comprometido > 0 AND fecha_comprometida IS NOT NULL) OR (resultado != 1 AND monto_comprometido IS NULL AND fecha_comprometida IS NULL)");
                    table.CheckConstraint("ck_seguimiento_cobranza_resultado", "resultado IN (1, 2, 3, 4)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_seguimiento_cobranza_cliente_fecha",
                schema: "cuentas_por_cobrar",
                table: "seguimiento_cobranza",
                columns: new[] { "cliente_id", "fecha" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_seguimiento_cobranza_promesas",
                schema: "cuentas_por_cobrar",
                table: "seguimiento_cobranza",
                column: "fecha_comprometida",
                filter: "resultado = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seguimiento_cobranza",
                schema: "cuentas_por_cobrar");
        }
    }
}
