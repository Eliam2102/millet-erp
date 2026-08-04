namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Sub-estado materializado de la dimensión Pago (diseño §3.bis.2, §5.2).
/// Los listeners del módulo Tesorería lo actualizan según los pagos
/// aplicados a las facturas del proveedor.
///
/// Persistido como <c>smallint</c> con CHECK constraint (0..2). El cierre
/// de la OC requiere <see cref="Pagada"/>.
/// </summary>
public enum SubEstadoPago : short
{
    SinPago = 0,
    Parcial = 1,
    Pagada = 2,
}
