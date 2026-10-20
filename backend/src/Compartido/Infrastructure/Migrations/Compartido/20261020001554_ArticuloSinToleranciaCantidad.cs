using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ArticuloSinToleranciaCantidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard: en piezas no hay tolerancia; no se borran valores configurados sin revisión.
            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM compartido.articulos " +
                "WHERE tolerancia_cantidad_porcentaje IS NOT NULL AND tolerancia_cantidad_porcentaje <> 0) THEN " +
                "RAISE EXCEPTION 'No se puede retirar la tolerancia de cantidad: existen articulos con tolerancia_cantidad_porcentaje distinta de 0. Revisar y poner en 0 o NULL antes de migrar.'; " +
                "END IF; END $$;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_articulos_tolerancia_rango",
                schema: "compartido",
                table: "articulos");

            migrationBuilder.DropColumn(
                name: "tolerancia_cantidad_porcentaje",
                schema: "compartido",
                table: "articulos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tolerancia_cantidad_porcentaje",
                schema: "compartido",
                table: "articulos",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_articulos_tolerancia_rango",
                schema: "compartido",
                table: "articulos",
                sql: "tolerancia_cantidad_porcentaje IS NULL OR (tolerancia_cantidad_porcentaje >= 0 AND tolerancia_cantidad_porcentaje <= 100)");
        }
    }
}
