namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Estatus del CFDI ante el SAT según la consulta de validez del comprobante
/// (distinto del <see cref="EstadoCfdiRecibido"/>, que es el ciclo de vida interno en el ERP).
/// </summary>
public enum EstatusSatCfdi
{
    Vigente   = 1,
    Cancelado = 2,
}
