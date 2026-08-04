namespace Millet.CuentasPorPagar.Domain.ComprobacionGastos;

/// <summary>
/// Beneficiario de la reposición de una comprobación de caja chica
/// (doc 12 §D2/Q1 — se soportan ambos destinos, elegido por el
/// capturista en el Sheet).
/// </summary>
public enum DestinoReposicionCaja
{
    /// <summary>La reposición se paga a la cuenta de la sucursal.</summary>
    CuentaSucursal = 1,

    /// <summary>La reposición se paga al empleado responsable de la caja.</summary>
    Responsable    = 2,
}
