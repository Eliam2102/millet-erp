using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Liberacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "autorizacion_credito",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supervisor_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    beneficiario_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    cliente_o_pedido_ref = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    fecha_autorizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vigente_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    decision_liberacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autorizacion_credito", x => x.id);
                    table.CheckConstraint("ck_autorizacion_credito_estado", "estado IN (1, 2, 3)");
                    table.CheckConstraint("ck_autorizacion_credito_no_autoconsumo", "supervisor_usuario_id != beneficiario_usuario_id");
                    table.CheckConstraint("ck_autorizacion_credito_uso_consistente", "(estado = 2 AND decision_liberacion_id IS NOT NULL) OR (estado != 2 AND decision_liberacion_id IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "decision_liberacion",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_ref = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    monto_pedido = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    credito_disponible_snapshot = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    resultado = table.Column<short>(type: "smallint", nullable: false),
                    regla_aplicada = table.Column<short>(type: "smallint", nullable: false),
                    override_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decidido_por = table.Column<Guid>(type: "uuid", nullable: false),
                    decidido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decision_liberacion", x => x.id);
                    table.CheckConstraint("ck_decision_liberacion_monto_positivo", "monto_pedido > 0");
                    table.CheckConstraint("ck_decision_liberacion_override_consistente", "(resultado = 3 AND override_id IS NOT NULL) OR (resultado != 3 AND override_id IS NULL)");
                    table.CheckConstraint("ck_decision_liberacion_regla", "regla_aplicada IN (1, 2, 3)");
                    table.CheckConstraint("ck_decision_liberacion_resultado", "resultado IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "regla_liberacion_serie",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prefijo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    comportamiento = table.Column<short>(type: "smallint", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regla_liberacion_serie", x => x.id);
                    table.CheckConstraint("ck_regla_liberacion_comportamiento", "comportamiento IN (1, 2, 3)");
                });

            migrationBuilder.InsertData(
                schema: "cuentas_por_cobrar",
                table: "regla_liberacion_serie",
                columns: new[] { "id", "activo", "comportamiento", "created_at", "created_by", "deleted_at", "prefijo", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("0000000a-1001-0000-0000-000000000001"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "5000", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-1001-0000-0000-000000000002"), true, (short)1, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "7000", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-1001-0000-0000-000000000003"), true, (short)2, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "3000", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-1001-0000-0000-000000000004"), true, (short)2, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "4000", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("0000000a-1001-0000-0000-000000000005"), true, (short)2, new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "8000", new DateTimeOffset(new DateTime(2026, 7, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_autorizacion_credito_beneficiario_estado",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito",
                columns: new[] { "beneficiario_usuario_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_decision_liberacion_cliente_fecha",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                columns: new[] { "cliente_id", "decidido_en" });

            migrationBuilder.CreateIndex(
                name: "ix_decision_liberacion_pedido",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                column: "pedido_ref");

            migrationBuilder.CreateIndex(
                name: "ux_regla_liberacion_prefijo",
                schema: "cuentas_por_cobrar",
                table: "regla_liberacion_serie",
                column: "prefijo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autorizacion_credito",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropTable(
                name: "decision_liberacion",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropTable(
                name: "regla_liberacion_serie",
                schema: "cuentas_por_cobrar");
        }
    }
}
