namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Ciclo de vida de un <see cref="MovimientoTarjetaCredito"/> (§3.2,
/// §3.3 del anexo TC). F7-PR4 implementa la transición inicial
/// <c>Registrado</c>; las demás (<c>ConciliadoConEstadoCuenta</c>,
/// <c>PagadoAlBanco</c>) entran en F7-PR5/PR6.
/// </summary>
public enum EstadoMovimientoTc
{
    Registrado                  = 1,
    ConciliadoConEstadoCuenta   = 2,
    EnDisputa                   = 3,
    Reversado                   = 4,
    PagadoAlBanco               = 5,
}
