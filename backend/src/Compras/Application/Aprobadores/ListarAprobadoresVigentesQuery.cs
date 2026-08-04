using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Aprobadores;

/// <summary>
/// Lista las asignaciones de aprobadores vigentes (sin <c>VigenteHasta</c>)
/// de la empresa actual. Filtros opcionales por departamento, rol o
/// usuario.
/// </summary>
public sealed record ListarAprobadoresVigentesQuery(
    Guid? DepartamentoId,
    RolAprobador? Rol,
    Guid? UsuarioId) : IRequest<IReadOnlyList<AprobadorVigenteResponse>>;

public sealed record AprobadorVigenteResponse(
    Guid Id,
    Guid DepartamentoId,
    RolAprobador Rol,
    Guid UsuarioId,
    DateTimeOffset VigenteDesde,
    Guid DesignadoPor,
    string? Motivo);

public sealed class ListarAprobadoresVigentesHandler
    : IRequestHandler<ListarAprobadoresVigentesQuery, IReadOnlyList<AprobadorVigenteResponse>>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public ListarAprobadoresVigentesHandler(ComprasDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
    }

    public async Task<IReadOnlyList<AprobadorVigenteResponse>> Handle(
        ListarAprobadoresVigentesQuery request, CancellationToken cancellationToken)
    {
        var query = request;
        var ct = cancellationToken;
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");

        var q = _db.AprobadoresDepartamento.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId && a.VigenteHasta == null);

        if (query.DepartamentoId is Guid d) q = q.Where(a => a.DepartamentoId == d);
        if (query.Rol is RolAprobador r) q = q.Where(a => a.Rol == r);
        if (query.UsuarioId is Guid u) q = q.Where(a => a.UsuarioId == u);

        return await q
            .OrderBy(a => a.DepartamentoId).ThenBy(a => a.Rol)
            .Select(a => new AprobadorVigenteResponse(
                a.Id, a.DepartamentoId, a.Rol, a.UsuarioId,
                a.VigenteDesde, a.DesignadoPor, a.Motivo))
            .ToListAsync(ct);
    }
}
