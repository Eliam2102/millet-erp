using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Catalogos;

/// <summary>
/// Lista los <see cref="MotivoRevision"/> activos. Lo consume la UI de
/// envío a revisión para mostrar el dropdown de motivos con SLA por
/// motivo (default 5 días, 15 para Disputa Contractual).
/// </summary>
public sealed record ListarMotivosRevisionQuery(bool IncluirInactivos = false) : IRequest<IReadOnlyList<MotivoRevisionResponse>>;

public sealed record MotivoRevisionResponse(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    int? SlaDias,
    string? DependenciaRevisoraDefaultCodigo,
    bool Activo);

public sealed class ListarMotivosRevisionHandler
    : IRequestHandler<ListarMotivosRevisionQuery, IReadOnlyList<MotivoRevisionResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public ListarMotivosRevisionHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<IReadOnlyList<MotivoRevisionResponse>> Handle(
        ListarMotivosRevisionQuery query,
        CancellationToken cancellationToken)
    {
        var q = _db.MotivosRevision.AsNoTracking();
        if (!query.IncluirInactivos)
            q = q.Where(m => m.Activo);

        return await q
            .OrderBy(m => m.Nombre)
            .Select(m => new MotivoRevisionResponse(
                m.Id, m.Codigo, m.Nombre, m.Descripcion,
                m.SlaDias, m.DependenciaRevisoraDefaultCodigo, m.Activo))
            .ToListAsync(cancellationToken);
    }
}
