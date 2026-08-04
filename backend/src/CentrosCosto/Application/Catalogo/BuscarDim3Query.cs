using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Asignaciones.Alcance;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Búsqueda server-side de Dim3 (CECO-PR4) — la query base del selector
// "Máquina" de la Fase E (molde funcional del backend de ArticuloSelector).
//
// Un solo parámetro Q que matchea clave O nombre (combobox único). Top-N
// sin offset (typeahead). Default SOLO ACTIVAS con opt-in de inactivas.
// CON filtro de alcance (CECO-PR6): el selector solo ofrece las Dim3
// asignadas al usuario actual — bypass con centros_costo.dim3.leer-todos;
// usuario sin filas = lista vacía. El listar del admin NO se filtra (por
// eso este endpoint es dedicado). Con la separación total el contexto
// Dim1 es un join local (sin puertos).
// ============================================================================

/// <summary>
/// Item del selector con el contexto completo para el display
/// "MCLC101 - Gantry (Corte · Conkal)" — nunca un GUID de cara al usuario.
/// </summary>
public sealed record Dim3BusquedaItem(
    Guid Id,
    string Clave,
    string Nombre,
    string GrupoDim3Nombre,
    string Dim2Clave,
    string Dim2Nombre,
    string Dim1Clave,
    string Dim1Nombre,
    EstatusCatalogo Estatus);

public sealed record BuscarDim3Query(
    string? Q,
    bool IncluirInactivas,
    int Limit) : IRequest<IReadOnlyList<Dim3BusquedaItem>>;

public sealed class BuscarDim3Handler
    : IRequestHandler<BuscarDim3Query, IReadOnlyList<Dim3BusquedaItem>>
{
    private readonly CentrosCostoDbContext _db;
    private readonly IAlcanceDim3Evaluator _alcance;

    public BuscarDim3Handler(CentrosCostoDbContext db, IAlcanceDim3Evaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<IReadOnlyList<Dim3BusquedaItem>> Handle(
        BuscarDim3Query request, CancellationToken cancellationToken)
    {
        // FILTRADO: el conjunto base pasa por el alcance del usuario actual.
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        return await Dim3SelectorProjection.EjecutarAsync(
            _db,
            alcance.AplicarA(_db.Dim3s.AsNoTracking()),
            request.Q, request.IncluirInactivas, request.Limit, cancellationToken);
    }
}
