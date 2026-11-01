using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Cajas.Terminal;

public sealed record TransaccionTerminalResponse(
    Guid Id, string NoOperacion, DateTime FechaHora, decimal Importe, string Moneda, string? Ultimos4,
    string? Autorizacion, string Estatus, EstadoTransaccionTerminal Estado, decimal? Comision, decimal? IvaComision,
    decimal? Neto, string? FolioLiquidacion, Guid? CobroMostradorId, string? FolioComprobante, Guid? LiquidacionId);

public sealed record TransaccionesTerminalPagina(IReadOnlyList<TransaccionTerminalResponse> Items, int Offset, int Limit, int Total);

/// <summary>Ventas de la terminal por estado (emparejadas, sin emparejar, rechazadas).</summary>
public sealed record TransaccionesTerminalQuery(EstadoTransaccionTerminal? Estado = null, int Offset = 0, int Limit = 50)
    : IRequest<TransaccionesTerminalPagina>;

/// <summary>Últimas cargas del archivo de la terminal con su resultado.</summary>
public sealed record CargasTerminalQuery(int Limit = 20) : IRequest<IReadOnlyList<CargaTerminalResponse>>;

public sealed class TerminalTarjetaQueryHandlers(FacturacionDbContext db) :
    IRequestHandler<TransaccionesTerminalQuery, TransaccionesTerminalPagina>,
    IRequestHandler<CargasTerminalQuery, IReadOnlyList<CargaTerminalResponse>>
{
    public async Task<TransaccionesTerminalPagina> Handle(TransaccionesTerminalQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);
        var q = db.TransaccionesTerminal.AsNoTracking();
        if (query.Estado is EstadoTransaccionTerminal estado) q = q.Where(t => t.Estado == estado);
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderByDescending(t => t.FechaHora).Skip(offset).Take(limit).ToListAsync(cancellationToken);

        var cobroIds = items.Where(t => t.CobroMostradorId != null).Select(t => t.CobroMostradorId!.Value).Distinct().ToList();
        var folios = await (from c in db.CobrosMostrador.AsNoTracking()
                            join cp in db.Comprobantes.AsNoTracking() on c.ComprobanteId equals cp.Id
                            where cobroIds.Contains(c.Id)
                            select new { c.Id, cp.Folio }).ToDictionaryAsync(x => x.Id, x => x.Folio, cancellationToken);

        return new TransaccionesTerminalPagina(items.Select(t => new TransaccionTerminalResponse(
            t.Id, t.NoOperacion, t.FechaHora, t.Importe, t.Moneda, t.Ultimos4, t.Autorizacion, t.Estatus, t.Estado,
            t.Comision, t.IvaComision, t.Neto, t.FolioLiquidacion, t.CobroMostradorId,
            t.CobroMostradorId is Guid id ? folios.GetValueOrDefault(id) : null, t.LiquidacionId)).ToList(), offset, limit, total);
    }

    public async Task<IReadOnlyList<CargaTerminalResponse>> Handle(CargasTerminalQuery query, CancellationToken cancellationToken) =>
        await db.CargasTerminal.AsNoTracking()
            .OrderByDescending(c => c.CargadoEn)
            .Take(Math.Clamp(query.Limit, 1, 100))
            .Select(c => new CargaTerminalResponse(c.Id, c.NombreArchivo, c.FechaArchivo, c.CargadoEn, c.Renglones, c.Nuevos,
                c.Duplicados, c.Emparejados, c.SinEmparejar, c.Rechazados, c.Liquidaciones))
            .ToListAsync(cancellationToken);
}
