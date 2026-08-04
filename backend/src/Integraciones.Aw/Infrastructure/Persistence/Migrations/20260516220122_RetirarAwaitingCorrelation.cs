using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetirarAwaitingCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PR #201: el flow callback per-EDI elimina el estado
            // AwaitingCorrelation (valor 1) como estado persistido. Tres
            // cambios coordinados:
            //
            // 1. Drop del índice ix_entidad_externa_correlation: lo usaba
            //    el polling AwCorrelationWorker (ahora retirado) para
            //    encontrar entidades aún sin aw_doc_id. El nuevo flow no
            //    polla — el outcome viene en la respuesta HTTP del drop.
            //
            // 2. Recreación del índice ix_entidad_externa_pendientes con
            //    filtro nuevo (0, 3, 4) en lugar de (0, 1, 3): el estado 1
            //    deja de existir como estado activo; 4 (FailedCorrelation)
            //    sí lo es ahora (entidad rechazada por A+W o stuck →
            //    operador la atiende desde UI bandeja).
            //
            // 3. UPDATE de remediación: filas existentes en estado=1 las
            //    movemos a estado=4 (FailedCorrelation) con un last_error
            //    marker. Sin esto quedarían huérfanas (nada las procesa
            //    nunca más). Idempotente — si no hay filas, no afecta.

            migrationBuilder.DropIndex(
                name: "ix_entidad_externa_correlation",
                schema: "integraciones_aw",
                table: "entidad_externa");

            migrationBuilder.DropIndex(
                name: "ix_entidad_externa_pendientes",
                schema: "integraciones_aw",
                table: "entidad_externa");

            // Remediación: AwaitingCorrelation (1) → FailedCorrelation (4)
            // con marker '[v2_migration]' en last_error. El operador puede
            // identificarlas en la bandeja y decidir Reintentar o
            // MarcarResuelto. NOTA: hacer ANTES de crear el nuevo index
            // (el índice nuevo no incluye estado=1, así que tenerlo
            // creado bloquearía el UPDATE de filas en 1).
            migrationBuilder.Sql(@"
                UPDATE integraciones_aw.entidad_externa
                SET estado = 4,
                    last_error = COALESCE(last_error, '') ||
                                 ' [v2_migration] estado migrado de AwaitingCorrelation(1) a FailedCorrelation(4) ' ||
                                 'por retiro del polling worker (PR #201). ' ||
                                 'Operador puede Reintentar (via MarcarResuelto primero) o MarcarResuelto.',
                    updated_at = NOW() AT TIME ZONE 'UTC'
                WHERE estado = 1;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_entidad_externa_pendientes",
                schema: "integraciones_aw",
                table: "entidad_externa",
                columns: new[] { "estado", "submitted_at" },
                filter: "estado IN (0, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversa estructural del índice. NOTA: NO revertimos el UPDATE
            // de remediación — las filas que se movieron a FailedCorrelation
            // se quedan así (no podemos distinguir las "remediadas" de las
            // que naturalmente llegaron a ese estado vía A+W rechazo).
            // Documentado en el comment del Up().
            migrationBuilder.DropIndex(
                name: "ix_entidad_externa_pendientes",
                schema: "integraciones_aw",
                table: "entidad_externa");

            migrationBuilder.CreateIndex(
                name: "ix_entidad_externa_correlation",
                schema: "integraciones_aw",
                table: "entidad_externa",
                columns: new[] { "tipo_entidad", "referencia_externa" },
                filter: "aw_doc_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entidad_externa_pendientes",
                schema: "integraciones_aw",
                table: "entidad_externa",
                columns: new[] { "estado", "submitted_at" },
                filter: "estado IN (0, 1, 3)");
        }
    }
}
