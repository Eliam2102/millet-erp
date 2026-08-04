namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Ciclo de vida de una <see cref="Tarjeta"/> (§3.3 del anexo TC,
/// F7-PR4). El estado <c>Bloqueada</c> es reversible (la TC se puede
/// reactivar); <c>Cancelada</c> es terminal.
/// </summary>
public enum EstadoTarjeta
{
    Activa      = 1,
    Bloqueada   = 2,
    Cancelada   = 3,
}
