using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Infrastructure.Persistence;
namespace Millet.Tesoreria.Application.PublicPorts;

/// <summary>
/// Fecha programada de pago por factura (P8.3, portal): la de la corrida autorizada que la incluye.
/// La corrida aún no guarda una fecha de pago propia; se usa el día de la autorización (o la fecha de ejecución de la línea).
/// </summary>
public sealed record ConsultarFechasProgramadasPagoQuery(IReadOnlyCollection<Guid> Facturas) : IRequest<IReadOnlyDictionary<Guid, DateOnly>>;
public sealed class ConsultarFechasProgramadasPagoHandler(TesoreriaDbContext db) : IRequestHandler<ConsultarFechasProgramadasPagoQuery, IReadOnlyDictionary<Guid, DateOnly>>
{
    public async Task<IReadOnlyDictionary<Guid, DateOnly>> Handle(ConsultarFechasProgramadasPagoQuery q, CancellationToken cancellationToken)
    {
        var filas = await (from l in db.Set<CorridaPagoLinea>().AsNoTracking()
            join c in db.CorridasPago.AsNoTracking() on l.CorridaId equals c.Id
            where l.Viva && q.Facturas.Contains(l.FacturaProveedorId) && c.Estado == EstadoCorridaPago.Autorizada && c.AutorizadaEn != null
            select new { l.FacturaProveedorId, l.FechaEjecucion, c.AutorizadaEn }).ToListAsync(cancellationToken);
        return filas.GroupBy(f => f.FacturaProveedorId).ToDictionary(g => g.Key,
            g => g.Max(f => f.FechaEjecucion ?? DateOnly.FromDateTime(f.AutorizadaEn!.Value.UtcDateTime)));
    }
}
