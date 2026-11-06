using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Application;
public sealed record ResolverAvisoDatos(string Resolucion, string? Motivo, Guid? DocumentoNuevoId = null, string? Soporte = null);
public sealed class ResolucionAvisosServicio(ActivosFijosDbContext db, ActivoServicio acceso, IClock clock)
{
    public async Task ResolverAsync(Guid activoId, Guid fuenteOAvId, ResolverAvisoDatos datos, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.documentos.gestionar", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({activoId.ToString()}, 0))", ct);
        var activo = await acceso.CargarAsync(activoId, true, ct);
        var aviso = await db.Avisos.SingleOrDefaultAsync(x => x.Id == fuenteOAvId && x.ActivoId == activoId, ct);
        var fuente = aviso?.FuenteId ?? fuenteOAvId;
        var documento = await db.Documentos.SingleOrDefaultAsync(x => x.Id == fuente && x.ActivoId == activoId, ct);
        var mantenimiento = documento == null ? await db.Mantenimientos.SingleOrDefaultAsync(x => x.Id == fuente && x.ActivoId == activoId, ct) : null;
        var vence = aviso?.Vencimiento ?? documento?.VigenteHasta ?? mantenimiento?.Proximo
            ?? throw new BusinessRuleException("AF_AVISO_NO_EXISTE", "No existe un vencimiento de este activo.");
        var avisos = await db.Avisos.Where(x => x.FuenteId == fuente && x.ActivoId == activoId && x.Vencimiento == vence).ToListAsync(ct);
        if (avisos.Any(x => x.ResueltoEn != null)) throw new BusinessRuleException("AF_AVISO_RESUELTO", "El vencimiento ya está resuelto.");
        if (avisos.Count == 0)
        {
            aviso = new(activo, fuente, mantenimiento != null, vence <= clock.HoyLocal() ? TipoAvisoActivo.Vencido : TipoAvisoActivo.PorVencer, vence, clock.HoyLocal());
            db.Avisos.Add(aviso); avisos.Add(aviso);
        }
        var nuevo = datos.DocumentoNuevoId is Guid id ? await db.Documentos.SingleOrDefaultAsync(x => x.Id == id && x.ActivoId == activoId, ct) : null;
        foreach (var v in avisos) v.Resolver(datos.Resolucion, datos.Motivo, nuevo, acceso.Actor, clock.UtcNow, datos.Soporte);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
