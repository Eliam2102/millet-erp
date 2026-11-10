using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Integration;

namespace Millet.ComercioExterior.Application;

/// <summary>
/// FAC-08 (86e3qdddf): un contenedor de exportación se embarcó (fecha real registrada). Lleva lo que
/// salió físicamente, agrupado por pedido y posición, para que Facturación registre una salida
/// facturable por pedido (no se factura el 100 % del pedido en cada contenedor). Consumidor:
/// Facturación, idempotente por (contenedor, pedido).
/// </summary>
public sealed record ContenedorEmbarcadoEvent(Guid Empresa, DateTimeOffset OcurridoEn,
    Guid ExpedienteId, string FolioExpediente, Guid ContenedorId, string NumeroContenedor, string BlGuia,
    DateOnly FechaEmbarqueReal, IReadOnlyList<PedidoContenedorEvento> Pedidos)
    : IntegrationEvent(ContenedorEmbarcadoEvent.Tipo, Empresa, OcurridoEn)
{
    public const string Tipo = "comercio_exterior.contenedor.embarcado.v1";

    /// <summary>Arma el evento con las partidas vigentes del contenedor; el número de pedido A+W viene de Facturación.</summary>
    public static ContenedorEmbarcadoEvent Desde(Expediente e, Guid contenedorId, IReadOnlyCollection<PedidoEmbarcable> pedidos, DateTimeOffset ahora)
    {
        var c = e.Contenedor(contenedorId);
        return new(e.EmpresaId, ahora, e.Id, e.Folio, c.Id, c.Numero!, c.BlGuia!, c.FechaEmbarqueReal!.Value,
            PedidoContenedorEvento.De(e, contenedorId, pedidos));
    }
}

public sealed record ContenedorPreparadoEvent(Guid Empresa, DateTimeOffset OcurridoEn,
    Guid ExpedienteId, string FolioExpediente, Guid ContenedorId, string NumeroContenedor, string BlGuia,
    IReadOnlyList<PedidoContenedorEvento> Pedidos)
    : IntegrationEvent(ContenedorPreparadoEvent.Tipo, Empresa, OcurridoEn)
{
    public const string Tipo = "comercio_exterior.contenedor.preparado.v1";

    public static ContenedorPreparadoEvent Desde(Expediente e, Guid id, IReadOnlyCollection<PedidoEmbarcable> pedidos, DateTimeOffset ahora)
    {
        var contenedor = e.Contenedor(id);
        return new(e.EmpresaId, ahora, e.Id, e.Folio, id, contenedor.Numero!, contenedor.BlGuia!,
            PedidoContenedorEvento.De(e, id, pedidos));
    }
}

/// <summary>
/// FAC-08: un contenedor ya embarcado se canceló. Facturación descarta la salida del contenedor si
/// no se ha facturado; si ya se facturó, la deja en la bandeja de excepciones para revisión.
/// </summary>
public sealed record ContenedorCanceladoEvent(Guid Empresa, DateTimeOffset OcurridoEn,
    Guid ExpedienteId, string FolioExpediente, Guid ContenedorId, string NumeroContenedor, string Motivo,
    IReadOnlyList<PedidoContenedorEvento> Pedidos)
    : IntegrationEvent(ContenedorCanceladoEvent.Tipo, Empresa, OcurridoEn)
{
    public const string Tipo = "comercio_exterior.contenedor.cancelado.v1";

    public static ContenedorCanceladoEvent Desde(Expediente e, Guid contenedorId, IReadOnlyCollection<PedidoEmbarcable> pedidos, DateTimeOffset ahora)
    {
        var c = e.Contenedor(contenedorId);
        return new(e.EmpresaId, ahora, e.Id, e.Folio, c.Id, c.Numero ?? "", c.MotivoCancelacion ?? "",
            PedidoContenedorEvento.De(e, contenedorId, pedidos));
    }
}

/// <summary>Pedido facturable del contenedor (id de Facturación y número A+W) con sus posiciones.</summary>
public sealed record PedidoContenedorEvento(Guid PedidoFacturableId, string NumeroPedido, IReadOnlyList<LineaContenedorEvento> Lineas)
{
    internal static IReadOnlyList<PedidoContenedorEvento> De(Expediente e, Guid contenedorId, IReadOnlyCollection<PedidoEmbarcable> pedidos) =>
        e.Partidas.Where(p => p.ContenedorId == contenedorId && p.Estado != EstadoPartida.Liberada)
            .GroupBy(p => p.PedidoFacturableId)
            .OrderBy(g => pedidos.FirstOrDefault(x => x.Id == g.Key)?.NumeroPedido, StringComparer.Ordinal).ThenBy(g => g.Key)
            .Select(g => new PedidoContenedorEvento(g.Key, pedidos.FirstOrDefault(x => x.Id == g.Key)?.NumeroPedido ?? "",
                g.GroupBy(p => p.Posicion).OrderBy(x => x.Key)
                    .Select(x => new LineaContenedorEvento(x.Key, x.Sum(p => p.Cantidad), string.Join("/", x.Select(p => p.Unidad).Distinct().Order())))
                    .ToArray()))
            .ToArray();
}

public sealed record LineaContenedorEvento(int Posicion, decimal Cantidad, string Unidad);
