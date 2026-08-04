using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Aprobadores;

/// <summary>
/// Histórico completo (vigentes + cerradas) de aprobadores en la empresa
/// actual. Filtros obligatorios (al menos uno de depto+rol o usuario)
/// para acotar el set; sin filtros podría retornar millones de filas a
/// largo plazo.
/// </summary>
public sealed record ListarAprobadoresHistoricoQuery(
    Guid? DepartamentoId,
    RolAprobador? Rol,
    Guid? UsuarioId) : IRequest<IReadOnlyList<AprobadorHistoricoResponse>>;

public sealed record AprobadorHistoricoResponse(
    Guid Id,
    Guid DepartamentoId,
    RolAprobador Rol,
    Guid UsuarioId,
    DateTimeOffset VigenteDesde,
    DateTimeOffset? VigenteHasta,
    Guid DesignadoPor,
    string? Motivo);

public sealed class ListarAprobadoresHistoricoHandler
    : IRequestHandler<ListarAprobadoresHistoricoQuery, IReadOnlyList<AprobadorHistoricoResponse>>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public ListarAprobadoresHistoricoHandler(ComprasDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
    }

    public async Task<IReadOnlyList<AprobadorHistoricoResponse>> Handle(
        ListarAprobadoresHistoricoQuery request, CancellationToken cancellationToken)
    {
        var query = request;
        var ct = cancellationToken;
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");

        // Exigir al menos un filtro para evitar full scan del histórico.
        if (query.DepartamentoId is null && query.UsuarioId is null && query.Rol is null)
        {
            throw new BusinessRuleException(
                "FILTRO_OBLIGATORIO",
                "Histórico requiere al menos un filtro (departamentoId, rol, o usuarioId).");
        }

        var q = _db.AprobadoresDepartamento.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId);

        if (query.DepartamentoId is Guid d) q = q.Where(a => a.DepartamentoId == d);
        if (query.Rol is RolAprobador r) q = q.Where(a => a.Rol == r);
        if (query.UsuarioId is Guid u) q = q.Where(a => a.UsuarioId == u);

        return await q
            .OrderByDescending(a => a.VigenteDesde)
            .Take(500)   // Cap defensivo.
            .Select(a => new AprobadorHistoricoResponse(
                a.Id, a.DepartamentoId, a.Rol, a.UsuarioId,
                a.VigenteDesde, a.VigenteHasta, a.DesignadoPor, a.Motivo))
            .ToListAsync(ct);
    }
}
