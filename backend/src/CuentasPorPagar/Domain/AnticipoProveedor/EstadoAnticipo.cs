namespace Millet.CuentasPorPagar.Domain.AnticipoProveedor;

/// <summary>
/// Estados de <see cref="AnticipoProveedor"/> (§4.5 del 00-levantamiento).
/// </summary>
public enum EstadoAnticipo
{
    Abierto    = 1,
    Amortizado = 2,
    Cancelado  = 3,
}
