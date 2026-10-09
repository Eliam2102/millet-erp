using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Recepciones;

internal static class RecepcionOcGuard
{
    public static async Task<IReadOnlyDictionary<Guid, OcLineaLectura>> ValidarAsync(
        OcLectura? oc, IReadOnlyList<RegistrarRecepcionLineaInput> entradas,
        IArticuloReadPort articulos, CancellationToken ct)
    {
        if (oc is null)
            throw new BusinessRuleException("RECEPCION_OC_NO_ENCONTRADA", "La orden de compra no existe; selecciona una orden autorizada.");
        // P2: el lector devuelve el estado tal cual (P1); aquí se da el motivo específico de la cancelación en curso.
        if (oc.Estado == "CancelacionSolicitada")
            throw new BusinessRuleException("OC_CANCELACION_SOLICITADA",
                "La orden de compra tiene una cancelación solicitada; Dirección debe resolverla antes de recibir mercancía.");
        if (oc.Estado != "Autorizada")
            throw new BusinessRuleException("RECEPCION_OC_NO_AUTORIZADA",
                $"La orden de compra está {EstadoDocumentoTexto.Describir(oc.Estado)}; no se puede recibir contra ella.");
        var lineas = oc.Lineas.ToDictionary(l => l.LineaId);
        foreach (var entrada in entradas)
        {
            if (entrada.LineaOcId is null || entrada.LineaOcId == Guid.Empty)
                throw new BusinessRuleException("RECEPCION_LINEA_OC_REQUERIDA", "Cada artículo recibido debe indicar su línea de orden de compra.");
            if (!lineas.TryGetValue(entrada.LineaOcId.Value, out var origen) || origen.ArticuloId != entrada.ArticuloId)
                throw new BusinessRuleException("RECEPCION_LINEA_OC_ARTICULO_INCONGRUENTE", "El artículo recibido no corresponde a la línea de la orden de compra seleccionada.");
            if (origen.PrecioUnitarioMxn <= 0)
                throw new BusinessRuleException("RECEPCION_COSTO_OC_INVALIDO", "La línea de la orden de compra no tiene un costo válido en pesos; corrige la orden antes de recibir.");
        }
        foreach (var grupo in entradas.GroupBy(l => l.LineaOcId!.Value))
        {
            var origen = lineas[grupo.Key];
            var articulo = await articulos.ObtenerAsync(origen.ArticuloId, ct);
            var limite = origen.CantidadSolicitada - origen.CantidadRecibida
                + origen.CantidadSolicitada * (articulo?.ToleranciaCantidadPorcentaje ?? 0m) / 100m;
            if (grupo.Sum(l => l.Cantidad) > limite)
                throw new BusinessRuleException("RECEPCION_EXCEDE_TOLERANCIA", $"La cantidad total recibida supera el saldo pendiente con tolerancia ({limite}) de la línea de la orden de compra.");
        }
        return lineas;
    }
}
