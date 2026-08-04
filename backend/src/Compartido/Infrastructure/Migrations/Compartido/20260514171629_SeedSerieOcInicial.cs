using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    /// <summary>
    /// F-Admin-PR6.2: placeholder de seed para la Serie OC inicial.
    ///
    /// <para>
    /// Esta migración NO inserta filas en producción para mantener el
    /// fallback al sistema legacy de <c>compras.folio_secuencias_oc</c>
    /// activo. El handler <c>CrearOrdenCompraVaciaHandler</c> detecta
    /// la ausencia de Serie activa (vía <c>SERIE_NO_CONFIGURADA</c>) y
    /// usa el path legacy, manteniendo el formato canónico actual
    /// <c>OC-{SucursalCodigo}{Anio}-{secuencial:D6}</c> que los tests
    /// existentes asumen (<c>OC-MID2026-NNNNNN</c>).
    /// </para>
    ///
    /// <para>
    /// PLATFORM-TODO(&lt;OcFolioMigrateToSeries&gt;): el cutover real al
    /// nuevo sistema de Series requiere:
    /// <list type="number">
    ///   <item>Decidir el nuevo formato canónico (¿incluye SucursalCodigo
    ///         en el prefijo? ¿Reinicio Anual o None?).</item>
    ///   <item>Actualizar el VO <c>Compras.Domain.Folio</c> para aceptar
    ///         el nuevo regex.</item>
    ///   <item>Insertar la fila Serie via una migración separada (e.g.
    ///         <c>SeedSerieOcActivar</c>) cuando los consumers downstream
    ///         estén listos.</item>
    ///   <item>Actualizar tests OC para esperar el nuevo formato.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// La migración existe para fijar el ID determinista de la Serie OC
    /// inicial (<c>00000006-0001-0000-0000-000000000001</c>) en el
    /// historial migracional — al crear la fila real, otra migración
    /// usará ese mismo Id para mantener idempotencia.
    /// </para>
    /// </summary>
    public partial class SeedSerieOcInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: ver doc-comment de la clase. Crear Series via
            // endpoints o futura migration activadora.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op simétrico.
        }
    }
}
