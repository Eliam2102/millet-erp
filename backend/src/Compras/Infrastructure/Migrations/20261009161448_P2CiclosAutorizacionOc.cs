using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class P2CiclosAutorizacionOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_oc_autorizaciones_autorizado",
                schema: "compras",
                table: "orden_compra_autorizaciones");

            migrationBuilder.AddColumn<int>(
                name: "ciclo_autorizacion",
                schema: "compras",
                table: "ordenes_compra",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ciclo",
                schema: "compras",
                table: "orden_compra_autorizaciones",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Conserva y clasifica las firmas existentes: cada rechazo cierra su ciclo.
            migrationBuilder.Sql("""
                WITH firmas AS (
                    SELECT id, 1 + COALESCE(SUM(CASE WHEN resultado = 2 THEN 1 ELSE 0 END)
                        OVER (PARTITION BY orden_compra_id ORDER BY fecha_hora, created_at, id
                            ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)::integer AS ciclo
                    FROM compras.orden_compra_autorizaciones
                )
                UPDATE compras.orden_compra_autorizaciones a SET ciclo = f.ciclo
                FROM firmas f WHERE a.id = f.id;

                UPDATE compras.ordenes_compra oc
                SET ciclo_autorizacion = GREATEST(1,
                    1 + (SELECT COUNT(*)::integer FROM compras.orden_compra_autorizaciones a
                        WHERE a.orden_compra_id = oc.id AND a.resultado = 2)
                    - CASE WHEN oc.estado = 6 THEN 1 ELSE 0 END);
                """);

            migrationBuilder.CreateIndex(
                name: "uq_oc_autorizaciones_autorizado",
                schema: "compras",
                table: "orden_compra_autorizaciones",
                columns: new[] { "orden_compra_id", "ciclo", "nivel" },
                unique: true,
                filter: "resultado = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El esquema anterior no representa firmas repetidas entre ciclos; no borrar historia.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM compras.orden_compra_autorizaciones
                        WHERE resultado = 1 GROUP BY orden_compra_id, nivel HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'No se puede revertir P2: existen firmas en varios ciclos. Conservar el historial y el esquema de ciclos.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "uq_oc_autorizaciones_autorizado",
                schema: "compras",
                table: "orden_compra_autorizaciones");

            migrationBuilder.DropColumn(
                name: "ciclo_autorizacion",
                schema: "compras",
                table: "ordenes_compra");

            migrationBuilder.DropColumn(
                name: "ciclo",
                schema: "compras",
                table: "orden_compra_autorizaciones");

            migrationBuilder.CreateIndex(
                name: "uq_oc_autorizaciones_autorizado",
                schema: "compras",
                table: "orden_compra_autorizaciones",
                columns: new[] { "orden_compra_id", "nivel" },
                unique: true,
                filter: "resultado = 1");
        }
    }
}
