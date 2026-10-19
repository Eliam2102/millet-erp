using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ClienteRfcUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No se puede decidir automáticamente cuál de dos clientes con el mismo RFC
            // conserva la identidad fiscal (pedidos y facturas quedan ligados a cada uno).
            // Detener antes de modificar filas y pedir conciliación explícita.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM compartido.clientes
                        WHERE rfc IS NOT NULL
                          AND upper(trim(rfc)) NOT IN ('XAXX010101000', 'XEXX010101000')
                        GROUP BY upper(trim(rfc))
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Hay RFC no genéricos duplicados en clientes; conciliar los registros antes de aplicar ClienteRfcUnico.';
                    END IF;
                END $$;
                """);

            // Backfill: normalizar RFC existente antes de exigir unicidad.
            migrationBuilder.Sql("UPDATE compartido.clientes SET rfc = upper(trim(rfc)) WHERE rfc IS NOT NULL AND rfc <> upper(trim(rfc));");

            migrationBuilder.DropIndex(
                name: "ix_clientes_rfc",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.CreateIndex(
                name: "ux_clientes_rfc_no_generico",
                schema: "compartido",
                table: "clientes",
                column: "rfc",
                unique: true,
                filter: "rfc NOT IN ('XAXX010101000','XEXX010101000')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_clientes_rfc_no_generico",
                schema: "compartido",
                table: "clientes");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_rfc",
                schema: "compartido",
                table: "clientes",
                column: "rfc");
        }
    }
}
