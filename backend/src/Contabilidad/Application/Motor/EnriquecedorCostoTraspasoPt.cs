using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// Traspaso de PT (M-249): pone en la salida o entrada el costo de las piezas movidas, leído por <see cref="ICostoProductoTerminadoReadPort"/> pedido por pedido.
/// El importe es el total de las piezas del movimiento (una sola línea contra el puente); la sucursal ya viene del evento. Sin valuación de un pedido
/// (PT5.12 aún no existe) o con una posición sin costo, el evento va a la bandeja de rechazos como dato faltante y se reprocesa desde ahí:
/// nunca se contabiliza en cero ni con un costo inventado, que es lo que garantiza que el puente cierre en cero.
/// </summary>
public sealed class EnriquecedorCostoTraspasoPt(ICostoProductoTerminadoReadPort? costos = null) : IEnriquecedorEventoContable
{
    public async Task<EventoContable> EnriquecerAsync(EventoContable evento, CancellationToken ct)
    {
        if (evento.Tipo is not (NormalizadorEventos.TraspasoPtSalidaAutorizada or NormalizadorEventos.TraspasoPtEntradaRegistrada)) return evento;

        var traspaso = NormalizadorEventos.LeerTraspasoPt(evento.Tipo, evento.Payload);
        var total = 0m;
        foreach (var pedido in traspaso.Pedidos!)
        {
            var piezas = (pedido.Posiciones ?? []).Where(p => p.Piezas > 0)
                .GroupBy(p => p.Posicion).Select(g => new PiezasPosicionPt(g.Key, g.Sum(p => p.Piezas))).OrderBy(p => p.Posicion).ToList();
            if (piezas.Count == 0) continue;

            var valuacion = costos is null ? null : await costos.CostoEntregaAsync(evento.EmpresaId, pedido.PedidoPtId, piezas, ct);
            if (valuacion is null)
                throw Faltante($"Dato faltante: costo de PT. El pedido {pedido.NumeroPedido} aún no tiene valuación de producto terminado; " +
                    $"la póliza del traspaso {traspaso.Folio} se genera al reprocesar cuando exista.");
            var porPosicion = valuacion.Posiciones.GroupBy(p => p.Posicion).ToDictionary(g => g.Key, g => g.First());
            var sinCosto = piezas.Where(p => !porPosicion.TryGetValue(p.Posicion, out var c) || c.Costo <= 0m).Select(p => p.Posicion).ToList();
            if (sinCosto.Count > 0)
                throw Faltante($"Dato faltante: costo de PT. Sin costo para la posición {string.Join(", ", sinCosto)} del pedido {pedido.NumeroPedido}.");
            total += piezas.Sum(p => Math.Round(porPosicion[p.Posicion].Costo, 2, MidpointRounding.AwayFromZero));
        }
        if (total == 0m) return evento; // el normalizador ya lo dejó sin movimiento

        return evento with { Importes = new Dictionary<string, decimal?> { ["Total"] = total } };
    }

    private static RechazoMotorException Faltante(string mensaje) => new(CausaRechazoContable.DatoFaltante, EnriquecedorCostoVentasPt.CampoDatoFaltante, mensaje);
}
