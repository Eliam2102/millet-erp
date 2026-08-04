namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Sub-estado materializado de la dimensión Facturación (diseño §3.bis.2,
/// §5.2). Los listeners del módulo CxP lo actualizan según las facturas
/// del proveedor registradas contra la OC.
///
/// Persistido como <c>smallint</c> con CHECK constraint (0..2).
/// </summary>
public enum SubEstadoFacturacion : short
{
    SinFactura = 0,
    Parcial = 1,
    Completa = 2,
}
