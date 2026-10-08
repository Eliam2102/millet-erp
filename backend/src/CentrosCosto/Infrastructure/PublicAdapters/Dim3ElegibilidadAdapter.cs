using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Asignaciones.Alcance;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Infrastructure.PublicAdapters;

/// <summary>
/// Adaptador productivo de <see cref="IDim3ElegibilidadPort"/> (G1.11 / ADR-0050).
/// Evalúa si un CC-Máquina existe, está activo y (si aplica) cae dentro del alcance
/// del usuario actual (<see cref="IAlcanceDim3Evaluator"/>).
/// </summary>
public sealed class Dim3ElegibilidadAdapter : IDim3ElegibilidadPort
{
    private readonly CentrosCostoDbContext _db;
    private readonly IAlcanceDim3Evaluator _alcance;

    public Dim3ElegibilidadAdapter(CentrosCostoDbContext db, IAlcanceDim3Evaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<Dim3Elegibilidad> EvaluarAsync(
        Guid dim3Id,
        bool aplicarAlcance,
        CancellationToken cancellationToken)
    {
        var estatus = await _db.Dim3s
            .AsNoTracking()
            .Where(d => d.Id == dim3Id)
            .Select(d => (EstatusCatalogo?)d.Estatus)
            .FirstOrDefaultAsync(cancellationToken);

        if (estatus is null)
        {
            return Dim3Elegibilidad.NoExiste;
        }

        if (estatus != EstatusCatalogo.Activo)
        {
            return Dim3Elegibilidad.Inactiva;
        }

        if (aplicarAlcance)
        {
            var alcance = await _alcance.ResolverAsync(cancellationToken);
            if (!alcance.EsTotal && !alcance.Dim3Ids.Contains(dim3Id))
            {
                return Dim3Elegibilidad.FueraDeAlcance;
            }
        }

        return Dim3Elegibilidad.Valida;
    }
}
