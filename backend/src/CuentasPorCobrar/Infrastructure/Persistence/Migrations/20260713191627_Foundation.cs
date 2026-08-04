using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cuentas_por_cobrar");

            migrationBuilder.CreateTable(
                name: "integration_events_outbox",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    integration_empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_events_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "linea_credito",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    limite = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    plazo_dias = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    motivo_bloqueo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linea_credito", x => x.id);
                    table.CheckConstraint("ck_linea_credito_bloqueo_consistente", "(estado = 2 AND motivo_bloqueo IS NOT NULL) OR (estado != 2)");
                    table.CheckConstraint("ck_linea_credito_estado", "estado IN (1, 2, 3)");
                    table.CheckConstraint("ck_linea_credito_limite_positivo", "limite > 0");
                    table.CheckConstraint("ck_linea_credito_moneda", "moneda IN ('MXN', 'USD')");
                    table.CheckConstraint("ck_linea_credito_origen", "origen IN (1, 2)");
                    table.CheckConstraint("ck_linea_credito_plazo_positivo", "plazo_dias > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_integration_empresa_id",
                schema: "cuentas_por_cobrar",
                table: "integration_events_outbox",
                column: "integration_empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_events_outbox_pending",
                schema: "cuentas_por_cobrar",
                table: "integration_events_outbox",
                column: "published_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_linea_credito_cliente_estado",
                schema: "cuentas_por_cobrar",
                table: "linea_credito",
                columns: new[] { "cliente_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_linea_credito_cliente_moneda_activa",
                schema: "cuentas_por_cobrar",
                table: "linea_credito",
                columns: new[] { "cliente_id", "moneda" },
                unique: true,
                filter: "estado = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_events_outbox",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropTable(
                name: "linea_credito",
                schema: "cuentas_por_cobrar");
        }
    }
}
