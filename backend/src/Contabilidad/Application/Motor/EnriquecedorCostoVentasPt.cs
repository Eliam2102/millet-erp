using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// PCV (86e3ke10b): pone en la entrega de PT el costo de lo entregado y la sucursal, leídos por <see cref="ICostoProductoTerminadoReadPort"/>.
/// Una línea por posición con piezas (cargo COSTO_DE_VENTAS por tipo de producto · abono INVENTARIO_PT). Sin valuación (PT5.12 aún no
/// existe) o con una posición sin costo, el evento va a la bandeja de rechazos como dato faltante; se reprocesa desde ahí. Nunca se
/// contabiliza en cero ni con un costo inventado.
/// </summary>
public sealed class EnriquecedorCostoVentasPt(ICostoProductoTerminadoReadPort? costos = null) : IEnriquecedorEventoContable
{
    public const string CampoDatoFaltante = "CostoPT";

    public async Task<EventoContable> EnriquecerAsync(EventoContable evento, CancellationToken ct)
    {
        if (evento.Tipo != NormalizadorEventos.EntregaPtConfirmada) return evento;

        var entrega = NormalizadorEventos.LeerEntregaPt(evento.Payload);
        var piezas = entrega.Entregadas!.Where(p => p.Piezas > 0)
            .GroupBy(p => p.Posicion).Select(g => new PiezasPosicionPt(g.Key, g.Sum(p => p.Piezas))).OrderBy(p => p.Posicion).ToList();
        if (piezas.Count == 0) return evento; // el normalizador ya la dejó sin movimiento

        var valuacion = costos is null ? null : await costos.CostoEntregaAsync(evento.EmpresaId, entrega.PedidoPtId, piezas, ct);
        if (valuacion is null)
            throw Faltante($"Dato faltante: costo de PT. El pedido {entrega.NumeroPedido} aún no tiene valuación de producto terminado; " +
                "la póliza de costo de ventas se genera al reprocesar cuando exista.");

        var porPosicion = valuacion.Posiciones.GroupBy(p => p.Posicion).ToDictionary(g => g.Key, g => g.First());
        var sinCosto = piezas.Where(p => !porPosicion.TryGetValue(p.Posicion, out var c) || c.Costo <= 0m).Select(p => p.Posicion).ToList();
        if (sinCosto.Count > 0)
            throw Faltante($"Dato faltante: costo de PT. Sin costo para la posición {string.Join(", ", sinCosto)} del pedido {entrega.NumeroPedido}.");

        var lineas = piezas.Select(p => porPosicion[p.Posicion])
            .Select(c => new LineaEventoContable(null, Math.Round(c.Costo, 2, MidpointRounding.AwayFromZero), null, false,
                Clasificador: string.IsNullOrWhiteSpace(c.TipoProducto) ? null : c.TipoProducto.Trim()))
            .ToList();
        return evento with
        {
            SucursalId = valuacion.SucursalId,
            Importes = new Dictionary<string, decimal?> { ["Total"] = lineas.Sum(l => l.Importe) },
            Lineas = lineas,
        };
    }

    private static RechazoMotorException Faltante(string mensaje) => new(CausaRechazoContable.DatoFaltante, CampoDatoFaltante, mensaje);
}
