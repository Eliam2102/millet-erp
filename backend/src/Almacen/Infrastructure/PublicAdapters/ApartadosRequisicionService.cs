using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Apartados;
using Millet.Almacen.Domain.Ports.Externos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

// Open Host Service: sólo Almacén escribe sus apartados. El llamador une la TX.
public sealed class ApartadosRequisicionService(AlmacenDbContext db, IAlmacenSaldoQueryPort saldos)
{
    private readonly Dictionary<(Guid, Guid), decimal> _salidasEnCurso = [];

    public async Task BloquearAsync(Guid sucursalId, IEnumerable<Guid> articulos, CancellationToken ct)
    {
        _salidasEnCurso.Clear();
        if (!db.Database.IsRelational()) return;
        foreach (var articulo in articulos.Distinct().Order())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({sucursalId.ToString() + articulo.ToString()}, 0))", ct);
    }

    public async Task<decimal> ApartarAsync(Guid empresaId, Guid sucursalId, Guid rqId,
        Guid lineaId, Guid articuloId, decimal solicitada, CancellationToken ct)
    {
        var disponible = await saldos.ConsultarDisponibilidadPorSucursalAsync(sucursalId, articuloId, ct);
        var cantidad = Math.Min(solicitada, disponible.CantidadDisponible);
        if (cantidad > 0)
        {
            db.ApartadosRequisicion.Add(new ApartadoRequisicion(empresaId, sucursalId, rqId,
                lineaId, articuloId, cantidad));
            await db.SaveChangesAsync(ct);
        }
        return cantidad;
    }

    public async Task LiberarAsync(Guid rqId, CancellationToken ct)
    {
        var apartados = await db.ApartadosRequisicion.Where(a => a.RequisicionId == rqId && a.Pendiente > 0).ToListAsync(ct);
        foreach (var a in apartados) a.Liberar();
        await db.SaveChangesAsync(ct);
    }

    public async Task ConsumirAsync(Guid sucursalId, Guid? rqId, Guid? lineaId,
        Guid articuloId, decimal cantidad, CancellationToken ct)
    {
        var apartado = rqId is null ? null : await db.ApartadosRequisicion
            .FirstOrDefaultAsync(a => a.RequisicionId == rqId && a.LineaRequisicionId == lineaId, ct);
        var disponible = await saldos.ConsultarDisponibilidadPorSucursalAsync(sucursalId, articuloId, ct);
        var propios = rqId is null ? 0m : await db.ApartadosRequisicion.AsNoTracking()
            .Where(a => a.RequisicionId == rqId && a.ArticuloId == articuloId).SumAsync(a => a.Pendiente, ct);
        var clave = (sucursalId, articuloId);
        var anteriores = _salidasEnCurso.GetValueOrDefault(clave);
        if (cantidad + anteriores > disponible.CantidadDisponible + propios)
            throw new BusinessRuleException("SALIDA_EXISTENCIA_APARTADA",
                "La existencia disponible no alcanza; parte del material está apartado para otra requisición.");
        _salidasEnCurso[clave] = anteriores + cantidad;
        apartado?.Consumir(cantidad);
    }
}
