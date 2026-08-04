namespace Millet.Compras.Application.Folios;

/// <summary>
/// Servicio de folios de requisición (extraído de <c>CrearRequisicionHandler</c> en
/// ADR-0047 PR5.C). Encapsula el upsert atómico sobre <c>compras.folio_secuencias</c>
/// y el formato canónico del folio, para que tanto el path de captura humana como el
/// path de creación por el sistema (motor de reorden) reusen la MISMA lógica sin
/// divergir en secuencia ni formato.
/// </summary>
public interface IFolioSecuenciaService
{
    /// <summary>
    /// Siguiente folio de requisición para <c>(empresa, sucursal, año)</c>, ya
    /// formateado como <c>{sucursalCodigo}{folioAnio}-{secuencial:D6}</c>. La
    /// secuencia es atómica (INSERT ... ON CONFLICT DO UPDATE RETURNING); la PK
    /// compuesta garantiza unicidad ante concurrencia.
    /// </summary>
    Task<string> SiguienteFolioRequisicionAsync(
        Guid empresaId,
        Guid sucursalId,
        string sucursalCodigo,
        short folioAnio,
        CancellationToken cancellationToken);
}
