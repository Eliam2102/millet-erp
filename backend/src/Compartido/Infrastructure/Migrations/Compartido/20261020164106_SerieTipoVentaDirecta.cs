using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class SerieTipoVentaDirecta : Migration
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

            migrationBuilder.AddCheckConstraint(
                name: "ck_series_vd_sucursal",
                schema: "compartido",
                table: "series",
                sql: "tipo_documento <> 6 OR sucursal_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropCheckConstraint(
                name: "ck_series_vd_sucursal",
                schema: "compartido",
                table: "series");

            migrationBuilder.CreateIndex(
                name: "ix_series_fiscal_activa_global",
                schema: "compartido",
                table: "series",
                columns: new[] { "empresa_id", "tipo_documento" },
                unique: true,
                filter: "activa AND sucursal_id IS NULL AND tipo_documento IN (2, 3, 5)");

            migrationBuilder.CreateIndex(
                name: "ix_series_fiscal_activa_sucursal",
                schema: "compartido",
                table: "series",
                columns: new[] { "empresa_id", "sucursal_id", "tipo_documento" },
                unique: true,
                filter: "activa AND sucursal_id IS NOT NULL AND tipo_documento IN (2, 3, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_series_tipo_documento",
                schema: "compartido",
                table: "series",
                sql: "tipo_documento BETWEEN 1 AND 5");
        }
    }
}
