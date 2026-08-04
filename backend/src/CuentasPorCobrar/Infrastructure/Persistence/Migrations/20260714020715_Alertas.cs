using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Alertas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alerta_cartera",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    detalle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    disparada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atendida = table.Column<bool>(type: "boolean", nullable: false),
                    atendida_por = table.Column<Guid>(type: "uuid", nullable: true),
                    atendida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerta_cartera", x => x.id);
                    table.CheckConstraint("ck_alerta_cartera_atencion_consistente", "(atendida = TRUE AND atendida_por IS NOT NULL AND atendida_en IS NOT NULL) OR (atendida = FALSE AND atendida_por IS NULL AND atendida_en IS NULL)");
                    table.CheckConstraint("ck_alerta_cartera_tipo", "tipo IN (1, 2, 3)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerta_cartera_dedupe",
                schema: "cuentas_por_cobrar",
                table: "alerta_cartera",
                columns: new[] { "cliente_id", "tipo", "moneda" },
                filter: "atendida = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_alerta_cartera_disparada",
                schema: "cuentas_por_cobrar",
                table: "alerta_cartera",
                column: "disparada_en");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerta_cartera",
                schema: "cuentas_por_cobrar");
        }
    }
}
