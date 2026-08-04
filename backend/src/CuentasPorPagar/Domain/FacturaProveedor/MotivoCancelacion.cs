namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

/// <summary>
/// Catálogo enumerado de motivos por los que una factura pasa a
/// <see cref="EstadoPasivo.Cancelada"/> (§4.2 del 01-diseno).
/// </summary>
public enum MotivoCancelacion
{
    RechazadaPorTolerancia = 1,
    CfdiCanceladoEnSat     = 2,
    ErrorCaptura           = 3,
    OtroConTexto           = 99,
}
