namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Tipo de comprobante fiscal según el catálogo SAT (CFDI 4.0 atributo
/// <c>cfdi:Comprobante@TipoDeComprobante</c>).
///
/// <para>
/// MVP soporta los 3 tipos relevantes para CxP: <see cref="Ingreso"/>
/// (factura del proveedor), <see cref="Egreso"/> (nota de crédito o
/// devolución), <see cref="Pago"/> (complemento de pago / REPP).
/// Otros tipos (Nómina, Traslado) se aceptan en parser pero no aplican
/// al flujo de CxP en v1.
/// </para>
/// </summary>
public enum TipoCfdi
{
    Desconocido = 0,
    Ingreso     = 1,
    Egreso      = 2,
    Pago        = 3,
    Traslado    = 4,
    Nomina      = 5,
}
