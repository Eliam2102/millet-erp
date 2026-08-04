using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Activos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Activos.Queries;

/// <summary>
/// Bandeja de autorizaciones de venta de activo fijo (B13, FE-F9). Por defecto
/// lista las <see cref="EstadoAutorizacionActivo.Autorizada"/> (disponibles para
/// emitir).
/// </summary>
public sealed record BandejaAutorizacionesActivoQuery(EstadoAutorizacionActivo? Estado)
    : IRequest<IReadOnlyList<AutorizacionActivoItem>>;

public sealed record AutorizacionActivoItem(
    Guid Id,
    string ActivoRef,
    string Descripcion,
    decimal PrecioVenta,
    decimal ValorNetoEnLibros,
    decimal UtilidadOPerdida,
    Guid AutorizadoPor,
    DateTimeOffset FechaAutorizacion,
    string Estado);

public sealed class BandejaAutorizacionesActivoHandler
    : IRequestHandler<BandejaAutorizacionesActivoQuery, IReadOnlyList<AutorizacionActivoItem>>
{
    private readonly FacturacionDbContext _db;
    public BandejaAutorizacionesActivoHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<AutorizacionActivoItem>> Handle(
        BandejaAutorizacionesActivoQuery query, CancellationToken cancellationToken)
    {
        var estado = query.Estado ?? EstadoAutorizacionActivo.Autorizada;

        var rows = await _db.AutorizacionesVentaActivo.AsNoTracking()
            .Where(a => a.Estado == estado)
            .OrderByDescending(a => a.FechaAutorizacion)
            .ToListAsync(cancellationToken);

        return rows
            .Select(a => new AutorizacionActivoItem(
                a.Id, a.ActivoRef, a.Descripcion, a.PrecioVenta, a.ValorNetoEnLibros, a.UtilidadOPerdida,
                a.AutorizadoPor, a.FechaAutorizacion, a.Estado.ToString()))
            .ToList();
    }
}
