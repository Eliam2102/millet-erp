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
        // M-158: la fracción que manda es la de Comercio Exterior; si no tiene 10 dígitos se detiene
        // antes de reservar folio y de llamar al PAC (la del cuerpo de la petición no cuenta).
        var invalida = ordenadas.FirstOrDefault(p => p.UnidadAduana.Trim() != "99"
            && !Millet.SharedKernel.Domain.FraccionArancelariaExportacion.EsValida(p.FraccionArancelaria));
        if (invalida is not null)
            throw new BusinessRuleException(Millet.SharedKernel.Domain.FraccionArancelariaExportacion.Codigo,
                $"Partida {invalida.Posicion}: {Millet.SharedKernel.Domain.FraccionArancelariaExportacion.Mensaje} " +
                "Corríjala en los datos aduanales del embarque en Comercio Exterior.");
        // H-U22: la unidad aduanera debe ser la UMT de la fracción; si no, el PAC rechaza (CCE161) con el folio ya gastado.
        foreach (var p in ordenadas)
            if (Millet.SharedKernel.Domain.UmtFraccionArancelaria.UmtEsperadaSiNoCoincide(p.FraccionArancelaria, p.UnidadAduana) is { } umt)
                throw new BusinessRuleException(Millet.SharedKernel.Domain.UmtFraccionArancelaria.Codigo,
                    $"Partida {p.Posicion}: {Millet.SharedKernel.Domain.UmtFraccionArancelaria.Mensaje(p.FraccionArancelaria, p.UnidadAduana, umt)} " +
                    "Corríjala en los datos aduanales del embarque en Comercio Exterior.");
        return lineas.Select((l, i) => l with { FraccionArancelaria = ordenadas[i].FraccionArancelaria, UnidadAduana = ordenadas[i].UnidadAduana }).ToArray();
    }
}
