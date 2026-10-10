using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Infrastructure.Persistence;
namespace Millet.Tesoreria.Application.PublicPorts;
public sealed record PagoProveedorLectura(Guid EmpresaId, Guid FacturaId, Guid PagoId, DateOnly Fecha, decimal Importe, decimal CubiertoRepp, bool Revertido);
public sealed record ConsultarPagosProveedorQuery(IReadOnlyCollection<Guid> Facturas) : IRequest<IReadOnlyList<PagoProveedorLectura>>;
public sealed class ConsultarPagosProveedorHandler(TesoreriaDbContext db) : IRequestHandler<ConsultarPagosProveedorQuery, IReadOnlyList<PagoProveedorLectura>>
{
    public async Task<IReadOnlyList<PagoProveedorLectura>> Handle(ConsultarPagosProveedorQuery q, CancellationToken cancellationToken)
    {
        var filas = await (from a in db.AplicacionesPagoProveedor.AsNoTracking()
            join m in db.MovimientosBancarios.AsNoTracking() on a.MovimientoId equals m.Id
            where q.Facturas.Contains(a.FacturaProveedorId)
            select new { m.EmpresaId, a.FacturaProveedorId, a.Id, m.FechaValor, a.ImporteAplicado, a.Revertida }).ToListAsync(cancellationToken);
        var ids = filas.Select(f => f.Id).ToArray();
        var cobertura = await db.ReppPagosProveedor.Where(r => ids.Contains(r.PagoId)).GroupBy(r => r.PagoId)
            .Select(g => new { Id = g.Key, Importe = g.Sum(r => r.Importe) }).ToDictionaryAsync(r => r.Id, r => r.Importe, cancellationToken);
        return filas.Select(f => new PagoProveedorLectura(f.EmpresaId, f.FacturaProveedorId, f.Id, f.FechaValor, f.ImporteAplicado, cobertura.GetValueOrDefault(f.Id), f.Revertida)).ToList();
    }
}
