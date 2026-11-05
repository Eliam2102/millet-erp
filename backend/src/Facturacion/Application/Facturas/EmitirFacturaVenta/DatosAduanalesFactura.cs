using Millet.ComercioExterior.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
public static class DatosAduanalesFactura
{
    public static IReadOnlyList<EmitirFacturaVentaCceLinea> Aplicar(IReadOnlyList<EmitirFacturaVentaCceLinea> lineas, IReadOnlyList<PartidaAduanalLectura>? revisadas)
    {
        if (revisadas is null || revisadas.Count != lineas.Count || revisadas.Any(p => string.IsNullOrWhiteSpace(p.FraccionArancelaria) || string.IsNullOrWhiteSpace(p.UnidadAduana)))
            throw new BusinessRuleException("CE_DATOS_ADUANALES_SIN_REVISAR", "Revise los datos aduanales de todas las partidas en Comercio Exterior antes de emitir la factura.");
        var ordenadas = revisadas.OrderBy(p => p.Posicion).ToArray();
        return lineas.Select((l, i) => l with { FraccionArancelaria = ordenadas[i].FraccionArancelaria, UnidadAduana = ordenadas[i].UnidadAduana }).ToArray();
    }
}
