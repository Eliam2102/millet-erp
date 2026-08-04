namespace Millet.Compras.Application.PreviewCubrimiento;

/// <summary>
/// Respuesta del preview de cubrimiento (PR-C). <see cref="Aplica"/> es
/// <c>false</c> (con <see cref="Lineas"/> vacía) cuando la RQ no está
/// <c>EnAutorizacion</c> — el FE solo pide el preview en ese estado, así que
/// esto es defensivo ante carreras (la RQ se autorizó entre el check y el
/// fetch). Las cantidades son una <b>estimación</b> al instante de la
/// consulta; no representan una reserva.
/// </summary>
public sealed record PreviewCubrimientoResponse(
    Guid RequisicionId,
    bool Aplica,
    IReadOnlyList<PreviewCubrimientoLinea> Lineas);

/// <summary>
/// Estimación por línea: cuánto se cubriría de almacén y cuánto iría a compra
/// según el <see cref="Disponible"/> actual, sin reservar.
/// </summary>
public sealed record PreviewCubrimientoLinea(
    Guid LineaId,
    Guid ArticuloId,
    string? ArticuloClave,
    string? ArticuloNombre,
    decimal Cantidad,
    decimal EstimadoDeAlmacen,
    decimal EstimadoDeCompra,
    decimal Disponible);
