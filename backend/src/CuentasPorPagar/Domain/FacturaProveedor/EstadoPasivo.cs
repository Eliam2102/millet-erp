namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

/// <summary>
/// Estados del agregado <see cref="FacturaProveedor"/> (§4.11 del
/// 00-levantamiento, A5 del 01-diseno §3 — modelo reducido a 5
/// estados ortogonales; el saldo parcial se ve por <c>SaldoPendiente</c>,
/// no por estado).
///
/// <list type="bullet">
///   <item><see cref="Capturada"/> — entró al ERP, conciliada con OC (si
///         tiene), pendiente de revisión / autorización.</item>
///   <item><see cref="EnRevision"/> — alguna dependencia revisora la
///         tiene atorada con motivo (§4 + §A22 del 01-diseno).</item>
///   <item><see cref="Autorizada"/> — lista para pago. Inmutable
///         (§A7 — error → cancelar y recapturar).</item>
///   <item><see cref="Pagada"/> — Tesorería liquidó el saldo completo.</item>
///   <item><see cref="Cancelada"/> — motivo explícito (rechazada por
///         tolerancia, CFDI cancelado en SAT, error de captura, etc.).</item>
/// </list>
/// </summary>
public enum EstadoPasivo
{
    Capturada  = 1,
    EnRevision = 2,
    Autorizada = 3,
    Pagada     = 4,
    Cancelada  = 5,
}
