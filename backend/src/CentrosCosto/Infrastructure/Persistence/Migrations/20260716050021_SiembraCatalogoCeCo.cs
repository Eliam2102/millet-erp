using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CentrosCosto.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Siembra del catálogo completo de Centros de Costo (CECO-PR5, modelo
    /// Dim): 6 grupos_dim2 + 44 grupos_dim3 + 5 dim1 + 57 dim2 + 361 dim3.
    /// SQL y documentación completa (fuente Excel, GUIDs congelados,
    /// idempotencia sin-target + guard final, receta de regeneración) en
    /// <see cref="SiembraCatalogoSql"/> (archivo generado por el script
    /// one-off del scratchpad — no editar a mano).
    ///
    /// <para>
    /// SINGLE-SCHEMA (separación total, levantamiento §7.4): no toca nada
    /// fuera de <c>centros_costo</c> — sin migración de Compartido, sin
    /// orden entre contextos, sin pre-flight de prod. Una sola llamada Sql
    /// dentro de la transacción de la migración: si el guard final hace
    /// RAISE, se revierte TODO — cero siembra a medias.
    /// </para>
    /// </summary>
    public partial class SiembraCatalogoCeCo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SiembraCatalogoSql.Sql);
        }

        /// <summary>
        /// Down deliberadamente vacío (mismo razonamiento que la siembra de
        /// #603): revertir el alta de un catálogo orgánico real es una
        /// operación manual con criterio humano — tras la Fase E habrá
        /// documentos apuntando a las dim3 sembradas; un DELETE automático
        /// fallaría por referencias o borraría catálogo vivo.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
