using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class SeriesFiscalesContinuidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "folio_inicial",
                schema: "compartido",
                table: "series",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            // P02 (U1.2): una sola serie fiscal activa por sucursal/tipo. Si una
            // BD heredada ya tiene duplicados, se detiene para conciliarlos a
            // mano (tools/u1.2-series-diagnostico.sql); no se borra historia.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM compartido.series
                        WHERE activa AND tipo_documento IN (2, 3, 5)
                        GROUP BY empresa_id, sucursal_id, tipo_documento
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Hay series fiscales activas duplicadas por sucursal y tipo; conciliarlas con tools/u1.2-series-diagnostico.sql antes de aplicar SeriesFiscalesContinuidad.';
                    END IF;
                END $$;
                """);

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

            migrationBuilder.DropColumn(
                name: "folio_inicial",
                schema: "compartido",
                table: "series");
        }
    }
}
