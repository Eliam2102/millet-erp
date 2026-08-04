using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Compras.Application.Oc.ObtenerHistorico;

public sealed class ObtenerHistoricoOrdenCompraHandler
    : IRequestHandler<ObtenerHistoricoOrdenCompraQuery, ObtenerHistoricoOrdenCompraResponse>
{
    private readonly ComprasDbContext _db;

    public ObtenerHistoricoOrdenCompraHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<ObtenerHistoricoOrdenCompraResponse> Handle(
        ObtenerHistoricoOrdenCompraQuery request, CancellationToken cancellationToken)
    {
        // Filtramos por AggregateRootId para incluir cambios sobre la OC
        // raíz y sus entidades hijas (líneas, adjuntos, autorizaciones,
        // PDF). Ver ADR-0008 § "audit por agregado".
        var eventos = await _db.Set<AuditLogEntry>()
            .AsNoTracking()
            .Where(a => a.AggregateRootId == request.OrdenCompraId
                && a.Modulo == "Compras"
                && (a.Entidad == "OrdenCompra"
                    || a.Entidad == "LineaOrdenCompra"
                    || a.Entidad == "AdjuntoOC"
                    || a.Entidad == "AutorizacionOC"
                    || a.Entidad == "OrdenCompraPdf"))
            .OrderBy(a => a.Timestamp)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.Operacion,
                a.Entidad,
                a.UsuarioId,
                a.Cambios,
            })
            .ToListAsync(cancellationToken);

        var mapped = eventos.Select(a => new EventoHistorico(
            a.Id,
            a.Timestamp,
            a.Operacion,
            a.Entidad,
            a.UsuarioId,
            a.Cambios.Length > 200 ? string.Concat(a.Cambios.AsSpan(0, 200), "...") : a.Cambios)).ToList();

        return new ObtenerHistoricoOrdenCompraResponse(mapped);
    }
}
