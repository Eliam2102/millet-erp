using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Cancelaciones.Queries;

/// <summary>
/// Bandeja del Contador General (F6.5): solicitudes de cancelación con su comprobante,
/// por estado (por omisión, las pendientes de autorización), de la más antigua a la
/// más reciente. Con <paramref name="Todos"/> trae todas, sin importar el estado, de la
/// más reciente a la más antigua (B3: la pestaña «Todos» de la bandeja).
/// </summary>
public sealed record BandejaCancelacionesQuery(EstadoSolicitudCancelacion? Estado, bool Todos = false) : IRequest<IReadOnlyList<CancelacionBandejaItem>>;

public sealed record CancelacionBandejaItem(
    Guid SolicitudId,
    Guid ComprobanteId,
    string TipoComprobante,
    string Folio,
    string? Uuid,
    string ReceptorRfc,
    string ReceptorNombre,
    decimal Total,
    string Moneda,
    string EstadoComprobante,
    string MotivoSat,
    string? UuidSustituto,
    string EstadoSolicitud,
    string? SolicitadaPor,
    DateTimeOffset SolicitadaEn,
    string? DecididaPor,
    DateTimeOffset? DecididaEn,
    string? MotivoRechazo,
    Guid? SolicitadaPorId = null);

public sealed class BandejaCancelacionesHandler(FacturacionDbContext db)
    : IRequestHandler<BandejaCancelacionesQuery, IReadOnlyList<CancelacionBandejaItem>>
{
    private const int Limite = 200;

    public async Task<IReadOnlyList<CancelacionBandejaItem>> Handle(BandejaCancelacionesQuery query, CancellationToken cancellationToken)
    {
        var estado = query.Estado ?? EstadoSolicitudCancelacion.PendienteAutorizacion;
        var solicitudes = db.SolicitudesCancelacion.AsNoTracking();
        if (!query.Todos)
            solicitudes = solicitudes.Where(s => s.Estado == estado);
        var unidas =
            from s in solicitudes
            join c in db.Comprobantes.AsNoTracking() on s.ComprobanteId equals c.Id
            select new { s, c };
        // «Todos» muestra primero lo más reciente: con el límite, las nuevas no se quedan fuera.
        var filas = await (query.Todos
                ? unidas.OrderByDescending(x => x.s.SolicitadaEn)
                : unidas.OrderBy(x => x.s.SolicitadaEn))
            .Take(Limite)
            .ToListAsync(cancellationToken);

        return filas.Select(f => new CancelacionBandejaItem(
            f.s.Id, f.c.Id, f.c.GetType().Name, f.c.Folio, f.c.Uuid, f.c.ReceptorRfc, f.c.ReceptorNombre,
            f.c.Total, f.c.Moneda, f.c.Estado.ToString(), f.s.MotivoSat, f.s.UuidSustituto, f.s.Estado.ToString(),
            f.s.SolicitadaPorNombre, f.s.SolicitadaEn, f.s.DecididaPorNombre, f.s.DecididaEn, f.s.MotivoRechazo,
            f.s.SolicitadaPorId)).ToList();
    }
}
