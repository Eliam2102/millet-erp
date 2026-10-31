using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Cancelaciones.Queries;

/// <summary>
/// Bandeja del Contador General (F6.5): solicitudes de cancelación con su comprobante,
/// por estado (por omisión, las pendientes de autorización), de la más antigua a la
/// más reciente.
/// </summary>
public sealed record BandejaCancelacionesQuery(EstadoSolicitudCancelacion? Estado) : IRequest<IReadOnlyList<CancelacionBandejaItem>>;

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
    string? MotivoRechazo);

public sealed class BandejaCancelacionesHandler(FacturacionDbContext db)
    : IRequestHandler<BandejaCancelacionesQuery, IReadOnlyList<CancelacionBandejaItem>>
{
    private const int Limite = 200;

    public async Task<IReadOnlyList<CancelacionBandejaItem>> Handle(BandejaCancelacionesQuery query, CancellationToken cancellationToken)
    {
        var estado = query.Estado ?? EstadoSolicitudCancelacion.PendienteAutorizacion;
        var filas = await (
            from s in db.SolicitudesCancelacion.AsNoTracking()
            join c in db.Comprobantes.AsNoTracking() on s.ComprobanteId equals c.Id
            where s.Estado == estado
            orderby s.SolicitadaEn
            select new { s, c })
            .Take(Limite)
            .ToListAsync(cancellationToken);

        return filas.Select(f => new CancelacionBandejaItem(
            f.s.Id, f.c.Id, f.c.GetType().Name, f.c.Folio, f.c.Uuid, f.c.ReceptorRfc, f.c.ReceptorNombre,
            f.c.Total, f.c.Moneda, f.c.Estado.ToString(), f.s.MotivoSat, f.s.UuidSustituto, f.s.Estado.ToString(),
            f.s.SolicitadaPorNombre, f.s.SolicitadaEn, f.s.DecididaPorNombre, f.s.DecididaEn, f.s.MotivoRechazo)).ToList();
    }
}
