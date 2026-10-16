using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Umbrales;

/// <summary>
/// Lista los umbrales N1/N2 vigentes (uno por departamento) de la empresa
/// actual. Vigente = <c>VigenteDesde &lt;= hoy</c> y
/// <c>VigenteHasta</c> nulo o &gt;= hoy.
/// </summary>
public sealed record ListarUmbralesQuery : IRequest<IReadOnlyList<UmbralAprobacionResponse>>;

public sealed class ListarUmbralesHandler : IRequestHandler<ListarUmbralesQuery, IReadOnlyList<UmbralAprobacionResponse>>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;

    public ListarUmbralesHandler(ComprasDbContext db, ICurrentEmpresaContext currentEmpresa, IClock clock)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
    }

    public async Task<IReadOnlyList<UmbralAprobacionResponse>> Handle(
        ListarUmbralesQuery query, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var hoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        return await _db.UmbralesAprobacionDepartamento
            .AsNoTracking()
            .Where(u => u.EmpresaId == empresaId
                        && u.VigenteDesde <= hoy
                        && (u.VigenteHasta == null || u.VigenteHasta >= hoy))
            .OrderBy(u => u.DepartamentoId)
            .Select(u => new UmbralAprobacionResponse(
                u.DepartamentoId, u.UmbralMenorCuantia, u.UmbralMonto, u.Moneda, u.VigenteDesde))
            .ToListAsync(cancellationToken);
    }
}
