using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class SerieExportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_series_fiscal_activa_global",
                schema: "compartido",
                table: "series");

            migrationBuilder.DropIndex(
                name: "ix_series_fiscal_activa_sucursal",
                schema: "compartido",
                table: "series");

            migrationBuilder.DropCheckConstraint(
                name: "ck_series_tipo_documento",
                schema: "compartido",
                table: "series");

            migrationBuilder.CreateIndex(
                name: "ix_series_fiscal_activa_global",
                schema: "compartido",
                table: "series",
                columns: new[] { "empresa_id", "tipo_documento" },
                unique: true,
                filter: "activa AND sucursal_id IS NULL AND tipo_documento IN (2, 3, 5, 6, 7)");

            migrationBuilder.CreateIndex(
                name: "ix_series_fiscal_activa_sucursal",
                schema: "compartido",
                table: "series",
                columns: new[] { "empresa_id", "sucursal_id", "tipo_documento" },
                unique: true,
                filter: "activa AND sucursal_id IS NOT NULL AND tipo_documento IN (2, 3, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_series_tipo_documento",
                schema: "compartido",
                table: "series",
                sql: "tipo_documento BETWEEN 1 AND 7");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Las series de exportación vuelven a CFDI inactivas: el CHECK anterior no admite el 7
            // y así se conserva su historial de folios.
            migrationBuilder.Sql("UPDATE compartido.series SET tipo_documento = 2, activa = false WHERE tipo_documento = 7;");

            migrationBuilder.DropIndex(
                name: "ix_series_fiscal_activa_global",
                schema: "compartido",
                table: "series");

            migrationBuilder.DropIndex(
                name: "ix_series_fiscal_activa_sucursal",
                schema: "compartido",
                table: "series");

            migrationBuilder.DropCheckConstraint(
                name: "ck_series_tipo_documento",
                schema: "compartido",
                table: "series");

            migrationBuilder.CreateIndex(
                name: "ix_series_fiscal_activa_global",
                schema: "compartido",
                table: "series",
                columns: new[] { "empresa_id", "tipo_documento" },
                unique: true,
                filter: "activa AND sucursal_id IS NULL AND tipo_documento IN (2, 3, 5, 6)");

            migrationBuilder.CreateIndex(
                name: "ix_series_fiscal_activa_sucursal",
                schema: "compartido",
                table: "series",
                columns: new[] { "empresa_id", "sucursal_id", "tipo_documento" },
                unique: true,
                filter: "activa AND sucursal_id IS NOT NULL AND tipo_documento IN (2, 3, 5, 6)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_series_tipo_documento",
                schema: "compartido",
                table: "series",
                sql: "tipo_documento BETWEEN 1 AND 6");
        }
    }
}
