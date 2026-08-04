using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReposicionesCajaChica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "destino_reposicion",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reposicion_id",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "configuracion_reposicion_caja",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monto_minimo = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_reposicion_caja", x => x.id);
                    table.CheckConstraint("ck_config_reposicion_minimo_no_negativo", "monto_minimo >= 0");
                });

            migrationBuilder.CreateTable(
                name: "reposiciones_caja_chica",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destino = table.Column<short>(type: "smallint", nullable: false),
                    beneficiario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    monto_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    numero_comprobaciones = table.Column<int>(type: "integer", nullable: false),
                    es_corte_manual = table.Column<bool>(type: "boolean", nullable: false),
                    emitida_por = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_emision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reposiciones_caja_chica", x => x.id);
                    table.CheckConstraint("ck_reposicion_destino", "destino IN (1, 2)");
                    table.CheckConstraint("ck_reposicion_monto_positivo", "monto_total > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_comprobaciones_reposicion_pendiente",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos",
                columns: new[] { "sucursal_id", "destino_reposicion", "reposicion_id" },
                filter: "reposicion_id IS NULL AND destino_reposicion IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_comprobacion_destino_reposicion",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos",
                sql: "destino_reposicion IS NULL OR destino_reposicion IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_config_reposicion_sucursal",
                schema: "cuentas_por_pagar",
                table: "configuracion_reposicion_caja",
                column: "sucursal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reposiciones_sucursal_fecha",
                schema: "cuentas_por_pagar",
                table: "reposiciones_caja_chica",
                columns: new[] { "sucursal_id", "fecha_emision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracion_reposicion_caja",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "reposiciones_caja_chica",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropIndex(
                name: "ix_comprobaciones_reposicion_pendiente",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_comprobacion_destino_reposicion",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos");

            migrationBuilder.DropColumn(
                name: "destino_reposicion",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos");

            migrationBuilder.DropColumn(
                name: "reposicion_id",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos");
        }
    }
}
