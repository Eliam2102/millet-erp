namespace Millet.Facturacion.Domain.Facturas;

/// <summary>
/// Ids bien conocidos del catálogo <c>compartido.canales_venta</c>
/// (FAC-ING-PR2). El catálogo es administrable, pero ciertos flujos del
/// backend fijan el canal por diseño (la ingesta de Planta Pintura siempre
/// factura por su propio canal). Los ids del seed 1..10 son espejo del enum
/// original y NUNCA se renumeran (ya persistidos en <c>canal_venta</c> de
/// <c>pedido_facturable</c> y <c>factura_venta</c>).
/// </summary>
public static class CanalesVentaConocidos
{
    /// <summary>Canal 9 "Planta Pintura" — fijo para la ingesta de Planta Pintura (§9, F10-PR2).</summary>
    public const short PlantaPintura = 9;
}
