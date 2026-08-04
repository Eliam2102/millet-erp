using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Búsqueda server-side de Dim3 SIN filtro de alcance — el selector ABIERTO
// de la captura por proxy (ADR-0050 §2): vale urgente (almacenista) y línea
// manual de OC (comprador). El despachador no es el dueño del gasto y debe
// poder cargar cualquier área, así que ve TODAS las máquinas activas.
//
// El "abrir" NO lo decide esta query: lo gatea el ENDPOINT que la expone, con
// el permiso que ya identifica a la población proxy (almacen.salidas.por-vale
// / compras.ordenes.crear-sin-rq) — esos endpoints llegan en PR3/PR5. Aquí,
// gemela de BuscarDim3Query pero pasando el conjunto base SIN AplicarA(alcance);
// por eso ni inyecta el evaluador (CentrosCosto sigue dependency-free en este
// camino). Comparte proyección y defaults con la filtrada vía
// Dim3SelectorProjection, y reusa Dim3BusquedaItem.
// ============================================================================

public sealed record BuscarDim3AbiertoQuery(
    string? Q,
    bool IncluirInactivas,
    int Limit) : IRequest<IReadOnlyList<Dim3BusquedaItem>>;

public sealed class BuscarDim3AbiertoHandler
    : IRequestHandler<BuscarDim3AbiertoQuery, IReadOnlyList<Dim3BusquedaItem>>
{
    private readonly CentrosCostoDbContext _db;

    public BuscarDim3AbiertoHandler(CentrosCostoDbContext db) => _db = db;

    public Task<IReadOnlyList<Dim3BusquedaItem>> Handle(
        BuscarDim3AbiertoQuery request, CancellationToken cancellationToken) =>
        // ABIERTO: el conjunto base son TODAS las Dim3, sin pasar por alcance.
        Dim3SelectorProjection.EjecutarAsync(
            _db,
            _db.Dim3s.AsNoTracking(),
            request.Q, request.IncluirInactivas, request.Limit, cancellationToken);
}
