using System.Globalization;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Recepciones;

internal sealed record RecepcionOcValidada(
    IReadOnlyDictionary<Guid, OcLineaLectura> Lineas,
    IReadOnlyDictionary<Guid, decimal> ToleranciaPorLinea,
    IReadOnlyDictionary<Guid, decimal> RecibidoPorLinea);

internal static class RecepcionOcGuard
{
    /// <summary>
    /// Límite único de recepción por línea de OC (F5). Lo usan el registro y la pantalla.
    /// El recibido es max(recibido en Compras, recibido propio de Almacén) para no
    /// depender solo del dato asíncrono de Compras.
    /// </summary>
    public static decimal Limite(decimal solicitada, decimal recibidaCompras, decimal recibidaPropia, decimal toleranciaPorcentaje)
        => solicitada * (1m + toleranciaPorcentaje / 100m) - Math.Max(recibidaCompras, recibidaPropia);

    public static async Task<RecepcionOcValidada> ValidarAsync(
        OcLectura? oc, IReadOnlyList<RegistrarRecepcionLineaInput> entradas,
        IArticuloReadPort articulos, IReadOnlyDictionary<Guid, decimal> recibidoPropio, CancellationToken ct)
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
            // CA2.10 (G1.12): el lector de OC no devuelve las líneas de servicio; los servicios no se reciben en Almacén.
            if (!lineas.TryGetValue(entrada.LineaOcId.Value, out var origen))
                throw new BusinessRuleException("RECEPCION_ARTICULO_NO_EN_OC", "La línea no se puede recibir en Almacén: no pertenece a la orden de compra o es un servicio, y los servicios no se reciben.");
            if (origen.ArticuloId != entrada.ArticuloId)
                throw new BusinessRuleException("RECEPCION_LINEA_OC_ARTICULO_INCONGRUENTE", "El artículo recibido no corresponde a la línea de la orden de compra seleccionada.");
            if (origen.PrecioUnitarioMxn <= 0)
                throw new BusinessRuleException("RECEPCION_COSTO_OC_INVALIDO", "La línea de la orden de compra no tiene un costo válido en pesos; corrige la orden antes de recibir.");
        }
        var tolerancias = new Dictionary<Guid, decimal>();
        var recibidos = new Dictionary<Guid, decimal>();
        foreach (var grupo in entradas.GroupBy(l => l.LineaOcId!.Value))
        {
            var origen = lineas[grupo.Key];
            var articulo = await articulos.ObtenerAsync(origen.ArticuloId, ct);
            var tol = articulo?.ToleranciaCantidadPorcentaje ?? 0m;
            var propio = recibidoPropio.GetValueOrDefault(grupo.Key);
            var limite = Limite(origen.CantidadSolicitada, origen.CantidadRecibida, propio, tol);
            if (grupo.Sum(l => l.Cantidad) > limite)
                throw new BusinessRuleException("RECEPCION_EXCEDE_TOLERANCIA",
                    $"La cantidad total recibida supera el saldo pendiente con tolerancia ({Math.Max(0m, limite).ToString("0.####", CultureInfo.InvariantCulture)}) de la línea de la orden de compra.");
            tolerancias[grupo.Key] = tol;
            recibidos[grupo.Key] = Math.Max(origen.CantidadRecibida, propio);
        }
        return new RecepcionOcValidada(lineas, tolerancias, recibidos);
    }
}
