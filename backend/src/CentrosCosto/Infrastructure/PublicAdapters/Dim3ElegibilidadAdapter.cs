using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Asignaciones.Alcance;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Infrastructure.PublicAdapters;

/// <remarks>
/// ADM08 / ADR-0062: el nombre histórico Dim3 se conserva por compatibilidad;
/// este contrato admite centros Dim1/Dim2 y máquinas Dim3.
/// </remarks>
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
        var nodos = await CentroCostoCatalogoLectura.ObtenerAsync(_db, cancellationToken);
        var nodo = nodos.SingleOrDefault(x => x.Id == dim3Id);
        if (nodo is null) return Dim3Elegibilidad.NoExiste;
        if (!nodo.Activo) return Dim3Elegibilidad.Inactiva;
        if (!aplicarAlcance) return Dim3Elegibilidad.Valida;
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        if (alcance.EsTotal) return Dim3Elegibilidad.Valida;
        var valido = nodo.Nivel == 3 ? alcance.Dim3Ids.Contains(nodo.Id) :
            nodos.Any(x => x.Nivel == 3 && x.Activo && alcance.Dim3Ids.Contains(x.Id) &&
                (nodo.Nivel == 2 ? x.Dim2Id == nodo.Id : x.Dim1Id == nodo.Id));
        return valido ? Dim3Elegibilidad.Valida : Dim3Elegibilidad.FueraDeAlcance;
    }
}
