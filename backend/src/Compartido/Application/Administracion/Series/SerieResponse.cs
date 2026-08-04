using Millet.Administracion.Domain;

namespace Millet.Administracion.Application.Series;

/// <summary>
/// DTO de respuesta de <see cref="Serie"/> (F-Admin-PR6.1). Mismo shape
/// en list, detail y mutaciones — el caller siempre recibe el agregado
/// completo para refrescar caches sin segundo fetch.
/// </summary>
public sealed record SerieResponse(
    Guid Id,
    Guid EmpresaId,
    Guid? SucursalId,
    TipoDocumentoSerie TipoDocumento,
    string Prefijo,
    string? Sufijo,
    ReinicioPeriodo ReinicioPeriodo,
    bool Activa,
    int Version);

/// <summary>
/// Detalle de serie con preview del próximo folio (F-Admin-PR6.1). El
/// preview se calcula a partir del último número actual de la secuencia
/// del período vigente — NO reserva el folio. El número real puede
/// diferir si entre el GET y el primer POST otra request consume folio.
/// </summary>
public sealed record SerieDetalleResponse(
    SerieResponse Serie,
    string ProximoFolioPreview);

/// <summary>Resultado de reservar un folio (<c>ReservarFolioCommand</c>).</summary>
public sealed record ReservarFolioResponse(
    string Folio,
    long Numero,
    string PeriodoClave);
