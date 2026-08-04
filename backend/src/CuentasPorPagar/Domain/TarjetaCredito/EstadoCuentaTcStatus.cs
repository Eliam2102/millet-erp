namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Ciclo de vida de un <see cref="EstadoCuentaTc"/> (§3.2 anexo TC,
/// F7-PR5). El cierre (<c>Cerrado</c>) requiere conciliación 100%
/// (diferencia = 0) y se hace desde F7-PR6.
/// </summary>
public enum EstadoCuentaTcStatus
{
    EnConciliacion  = 1,
    Conciliado      = 2,
    Cerrado         = 3,
    PagadoBanco     = 4,
}
