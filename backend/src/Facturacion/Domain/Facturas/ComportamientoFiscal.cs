namespace Millet.Facturacion.Domain.Facturas;

/// <summary>
/// Eje <b>fiscal</b> de la facturación: cómo se comporta la venta frente al SAT
/// (§7, D3). Ortogonal al <see cref="CanalVenta"/> — un mismo canal puede tener
/// distintos comportamientos.
///
/// <para>El valor numérico (<c>short</c>) está fijo por ABI — agregar nuevos al
/// final, nunca renumerar.</para>
/// </summary>
public enum ComportamientoFiscal : short
{
    MostradorInmediato = 1,
    ConAnticipo = 2,
    ExportacionConCce = 3,
    TrasladoConCartaPorte = 4,
    VentaActivoFijo = 5,
    Administrativa = 6,
}
