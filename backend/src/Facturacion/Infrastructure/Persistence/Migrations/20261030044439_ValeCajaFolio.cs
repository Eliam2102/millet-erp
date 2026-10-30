using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ValeCajaFolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "caja_id",
                schema: "facturacion",
                table: "caja_movimiento",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "folio_vale",
                schema: "facturacion",
                table: "caja_movimiento",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ultimo_folio_vale",
                schema: "facturacion",
                table: "caja",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ux_caja_movimiento_folio_vale",
                schema: "facturacion",
                table: "caja_movimiento",
                columns: new[] { "caja_id", "folio_vale" },
                unique: true,
                filter: "folio_vale IS NOT NULL");

            // Retiros ya registrados: folio consecutivo por caja en orden de captura
            // para que también se puedan imprimir; el contador de la caja sigue desde ahí.
            migrationBuilder.Sql("""
                WITH numerados AS (
                    SELECT m.id, s.caja_id,
                           ROW_NUMBER() OVER (PARTITION BY s.caja_id ORDER BY m.created_at, m.id) AS folio
                    FROM facturacion.caja_movimiento m
                    JOIN facturacion.caja_sesion s ON s.id = m.caja_sesion_id
                    WHERE m.tipo = 3)
                UPDATE facturacion.caja_movimiento m
                SET caja_id = n.caja_id, folio_vale = n.folio
                FROM numerados n
                WHERE m.id = n.id;

                UPDATE facturacion.caja c
                SET ultimo_folio_vale = t.ultimo
                FROM (SELECT caja_id, MAX(folio_vale) AS ultimo
                      FROM facturacion.caja_movimiento
                      WHERE folio_vale IS NOT NULL
                      GROUP BY caja_id) t
                WHERE c.id = t.caja_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_caja_movimiento_folio_vale",
                schema: "facturacion",
                table: "caja_movimiento");

            migrationBuilder.DropColumn(
                name: "caja_id",
                schema: "facturacion",
                table: "caja_movimiento");

            migrationBuilder.DropColumn(
                name: "folio_vale",
                schema: "facturacion",
                table: "caja_movimiento");

            migrationBuilder.DropColumn(
                name: "ultimo_folio_vale",
                schema: "facturacion",
                table: "caja");
        }
    }
}
