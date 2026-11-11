using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Ingesta.Queries;

/// <summary>
/// M-233: envíos de A+W que siguen sin acuse en <c>aw_solicitud_pedido</c> (resultado vacío o pospuesto).
/// <c>Incierto</c> = el ERP ya aplicó esa versión pero el acuse a A+W no quedó (envío interrumpido; el siguiente
/// ciclo lo confirma sin reprocesar). <c>EnCurso</c> = el ERP todavía no la aplica; si hay excepción abierta va su detalle.
/// </summary>
public sealed record EnviosAwInciertosQuery(int Max = 200) : IRequest<IReadOnlyList<EnvioAwInciertoItem>>;

public sealed record EnvioAwInciertoItem(
    Guid SolicitudId, string NumeroPedido, string Operacion, long Version, DateTimeOffset CreadaAt,
    string Estado, long? VersionAplicadaErp, string? Detalle);

public sealed class EnviosAwInciertosHandler(FacturacionDbContext db, IAwSolicitudesReader reader)
    : IRequestHandler<EnviosAwInciertosQuery, IReadOnlyList<EnvioAwInciertoItem>>
{
    public async Task<IReadOnlyList<EnvioAwInciertoItem>> Handle(EnviosAwInciertosQuery query, CancellationToken cancellationToken)
    {
        var pendientes = await reader.LeerPendientesAsync(Math.Clamp(query.Max, 1, 500), cancellationToken);
        if (pendientes.Count == 0) return [];
        var pedidos = pendientes.Select(p => p.NumeroPedido).Distinct().ToList();
        var controles = await db.IngestaControles.AsNoTracking()
            .Where(c => c.Origen == OrigenPedido.Aw && pedidos.Contains(c.ClaveNatural))
            .ToDictionaryAsync(c => c.ClaveNatural, c => c.UltimaVersionAplicada, cancellationToken);
        var excepciones = await db.ExcepcionesImportacion.AsNoTracking()
            .Where(e => e.Origen == OrigenPedido.Aw && !e.Resuelto && pedidos.Contains(e.PedidoRef))
            .GroupBy(e => e.PedidoRef).Select(g => new { g.Key, Detalle = g.OrderByDescending(e => e.CreatedAt).First().Detalle })
            .ToDictionaryAsync(e => e.Key, e => e.Detalle, cancellationToken);

        return pendientes
            .Select(p =>
            {
                long? aplicada = controles.TryGetValue(p.NumeroPedido, out var v) ? v : null;
                var incierto = aplicada >= p.Version;
                return new EnvioAwInciertoItem(p.SolicitudId, p.NumeroPedido, p.Operacion.ToString(), p.Version, p.CreadaAt,
                    incierto ? "Incierto" : "EnCurso", aplicada,
                    incierto ? "El ERP ya aplicó esta versión; falta el acuse en A+W (se confirma en el siguiente ciclo)."
                        : excepciones.GetValueOrDefault(p.NumeroPedido));
            })
            .OrderByDescending(i => i.Estado == "Incierto").ThenBy(i => i.CreadaAt)
            .ToList();
    }
}
