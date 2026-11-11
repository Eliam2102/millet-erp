using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Contabilidad.Application.Periodos;

/// <summary>
/// Evidencia de la reapertura acotada de un periodo (M-213; expediente G1.2). Se adjunta mientras la reapertura está vigente; al
/// concluir (periodo cerrado de nuevo) la evidencia queda de solo lectura. La reapertura es de la empresa: sin alcance por sucursal.
/// </summary>
public sealed class ReaperturaAdjuntoPropietario(ContabilidadDbContext db) : IAdjuntoPropietario
{
    public const string Tipo = "reapertura-periodo";

    public string TipoEntidad => Tipo;
    public string CodigoNoEncontrado => "CONTAB_REAPERTURA_NO_ENCONTRADA";

    // Literales (Contabilidad no referencia Identidad): PermisosCanonicos.ContabilidadPeriodo*.
    public string PermisoVer => "contabilidad.periodo.leer";
    public string PermisoSubir => "contabilidad.periodo.reabrir";
    public string PermisoBaja => "contabilidad.periodo.reabrir";

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken) =>
        await db.Reaperturas.AsNoTracking().Where(x => x.Id == entidadId)
            .Select(x => new { x.Id, x.EmpresaId, x.Vigente, x.Anio, x.Numero })
            .FirstOrDefaultAsync(cancellationToken) is { } r
            ? new AdjuntoPropietarioInfo(r.Id, r.EmpresaId, null, r.Vigente, null, $"Reapertura {r.Anio}-{r.Numero:00}")
            : null;

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken) => Task.CompletedTask;
}
