using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.Compras.Infrastructure;

namespace Millet.Api.Seed;

public sealed partial class DemoSesionSeedHostedService
{
    private static async Task SembrarAlmacenAsync(IServiceProvider sp, CancellationToken ct)
    {
        var compras = sp.GetRequiredService<ComprasDbContext>();
        var db = sp.GetRequiredService<AlmacenDbContext>();
        var ocMidId = Id("DEMO-OC-MID");
        var ocMtyId = Id("DEMO-OC-MTY");
        var rqMidId = Id("DEMO-RQ-MID");
        var ordenes = await compras.OrdenesCompra.AsNoTracking().Include(o => o.Lineas)
            .Where(o => o.EmpresaId == EmpresaId && (o.Id == ocMidId || o.Id == ocMtyId ||
                (o.ReferenciaProveedor != null && o.ReferenciaProveedor.StartsWith("DEMO-"))))
            .ToListAsync(ct);
        var requisiciones = await compras.Requisiciones.AsNoTracking().Include(r => r.Lineas)
            .Where(r => r.EmpresaId == EmpresaId && (r.Id == rqMidId ||
                (r.Descripcion != null && r.Descripcion.StartsWith("DEMO-"))))
            .ToListAsync(ct);
        var articulosPorSucursal = ordenes.SelectMany(o => o.Lineas.Where(l => !l.EsServicio)
                .Select(l => (SucursalId: o.SucursalDestinoId, l.ArticuloId)))
            .Concat(requisiciones.SelectMany(r => r.Lineas.Select(l => (r.SucursalId, l.ArticuloId))))
            .Distinct().GroupBy(x => x.SucursalId);

        foreach (var sucursal in articulosPorSucursal)
        {
            var sub = await (from s in db.SubAlmacenes
                             join a in db.Almacenes on s.AlmacenId equals a.Id
                             where a.SucursalId == sucursal.Key && s.Clave == "INSUMOS"
                             orderby a.Clave
                             select s).FirstAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rack = await db.Ubicaciones.SingleOrDefaultAsync(u => u.SubAlmacenId == sub.Id && u.Clave == "R-01", ct);
            if (rack is null)
            {
                rack = new Ubicacion(Id($"DEMO-RACK/{sub.Id}/R-01"), sub.Id, "R-01", "Rack 1 DEMO");
                db.Ubicaciones.Add(rack);
            }
            rack.CambiarEstatus(EstatusCatalogo.Activo);
            foreach (var (_, articuloId) in sucursal)
            {
                var asignacion = await db.AsignacionesArticuloUbicacion
                    .SingleOrDefaultAsync(a => a.UbicacionId == rack.Id && a.ArticuloId == articuloId, ct);
                if (asignacion is null)
                    db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(
                        Id($"DEMO-ASIGNACION/{rack.Id}/{articuloId}"), rack.Id, articuloId));
                else asignacion.CambiarEstatus(EstatusCatalogo.Activo);
                // La asignación crea el saldo en cero; los reinicios preservan cualquier existencia.
                if (!await db.SaldosInventario.AnyAsync(s => s.UbicacionId == rack.Id && s.ArticuloId == articuloId, ct))
                    db.SaldosInventario.Add(new SaldoInventario(rack.Id, sub.Id, articuloId, 0, 0));
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
