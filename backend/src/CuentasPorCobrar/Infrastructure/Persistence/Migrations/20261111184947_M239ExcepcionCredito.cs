using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M239ExcepcionCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_autorizacion_credito_estado",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito");

            migrationBuilder.AddColumn<short>(
                name: "condicion",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "firmas_requeridas",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<string>(
                name: "moneda",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "MXN");

            migrationBuilder.AddColumn<decimal>(
                name: "monto_autorizado",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "segunda_firma_en",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "segunda_firma_por_id",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "regla_firma_excepcion_credito",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    umbral_mxn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    firmas_requeridas = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regla_firma_excepcion_credito", x => x.id);
                    table.CheckConstraint("ck_regla_firma_excepcion_credito_firmas", "firmas_requeridas IN (1, 2)");
                    table.CheckConstraint("ck_regla_firma_excepcion_credito_umbral", "umbral_mxn >= 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_autorizacion_credito_estado",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito",
                sql: "estado IN (1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "ux_regla_firma_excepcion_credito",
                schema: "cuentas_por_cobrar",
                table: "regla_firma_excepcion_credito",
                columns: new[] { "empresa_id", "umbral_mxn" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "regla_firma_excepcion_credito",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropCheckConstraint(
                name: "ck_autorizacion_credito_estado",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito");

            migrationBuilder.DropColumn(
                name: "condicion",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito");

            migrationBuilder.DropColumn(
                name: "firmas_requeridas",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito");

            migrationBuilder.DropColumn(
                name: "moneda",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito");

            migrationBuilder.DropColumn(
                name: "monto_autorizado",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito");

            migrationBuilder.DropColumn(
                name: "segunda_firma_en",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito");

            migrationBuilder.DropColumn(
                name: "segunda_firma_por_id",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito");

            migrationBuilder.AddCheckConstraint(
                name: "ck_autorizacion_credito_estado",
                schema: "cuentas_por_cobrar",
                table: "autorizacion_credito",
                sql: "estado IN (1, 2, 3)");
        }
    }
}
