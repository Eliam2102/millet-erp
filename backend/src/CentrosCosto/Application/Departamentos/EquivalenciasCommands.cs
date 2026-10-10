using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.CentrosCosto.Application.Departamentos;

public sealed record EquivalenciaResponse(Guid SucursalId, Guid DepartamentoId, string Departamento,
    Guid? CentroCostoId, string? CentroCosto, string? Observaciones, int Version);
public sealed record ListarEquivalenciasQuery(Guid SucursalId) : IRequest<IReadOnlyList<EquivalenciaResponse>>;
public sealed record GuardarEquivalenciaCommand(Guid SucursalId, Guid DepartamentoId, Guid CentroCostoId,
    string Observaciones, int VersionEsperada) : IRequest<Unit>;
public sealed class ListarEquivalenciasValidator : AbstractValidator<ListarEquivalenciasQuery>
{
    public ListarEquivalenciasValidator() => RuleFor(x => x.SucursalId).NotEmpty();
}
public sealed class GuardarEquivalenciaValidator : AbstractValidator<GuardarEquivalenciaCommand>
{
    public GuardarEquivalenciaValidator()
    {
        RuleFor(x => x.SucursalId).NotEmpty(); RuleFor(x => x.DepartamentoId).NotEmpty();
        RuleFor(x => x.CentroCostoId).NotEmpty(); RuleFor(x => x.Observaciones).NotEmpty().MaximumLength(500);
        RuleFor(x => x.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}
public sealed class EquivalenciasHandler(CentrosCostoDbContext db, CompartidoDbContext organizacion,
    ICurrentUserContext user, ICurrentEmpresaContext empresa, ICurrentUserPermissions permisos,
    IUsuarioSucursalReadPort sucursales, IDim3ReadPort lectura)
    : IRequestHandler<ListarEquivalenciasQuery, IReadOnlyList<EquivalenciaResponse>>,
      IRequestHandler<GuardarEquivalenciaCommand, Unit>
{
    private Task VerificarAsync(Guid sucursalId, CancellationToken cancellationToken) => SucursalScopeGuard.VerificarAsync(
        user.UserId, "centros_costo.catalogo.administrar", permisos,
        (uid, token) => sucursales.EstaAsociadoAsync(uid, sucursalId, token), cancellationToken);
    public async Task<IReadOnlyList<EquivalenciaResponse>> Handle(ListarEquivalenciasQuery request, CancellationToken cancellationToken)
    {
        await VerificarAsync(request.SucursalId, cancellationToken);
        var departamentos = await (from a in organizacion.SucursalDepartamentos.AsNoTracking()
            join d in organizacion.Departamentos.AsNoTracking() on a.DepartamentoId equals d.Id
            where a.SucursalId == request.SucursalId && a.Estatus == EstatusCatalogo.Activo && d.Estatus == EstatusCatalogo.Activo
            select new { d.Id, d.Nombre }).ToListAsync(cancellationToken);
        var eq = await db.DepartamentoCentrosCosto.AsNoTracking().Where(x => x.SucursalId == request.SucursalId).ToListAsync(cancellationToken);
        var ids = eq.Select(x => x.CentroCostoId).ToArray();
        var nodos = await lectura.ObtenerAsync(ids, cancellationToken);
        return departamentos.OrderBy(d => d.Nombre).Select(d => {
            var e = eq.SingleOrDefault(x => x.DepartamentoId == d.Id);
            var nombre = e is not null && nodos.TryGetValue(e.CentroCostoId, out var n) ? $"{n.Clave} — {n.Nombre}" : null;
            return new EquivalenciaResponse(request.SucursalId, d.Id, d.Nombre, e?.CentroCostoId, nombre, e?.Observaciones, e?.Version ?? 0);
        }).ToArray();
    }
    public async Task<Unit> Handle(GuardarEquivalenciaCommand request, CancellationToken cancellationToken)
    {
        await VerificarAsync(request.SucursalId, cancellationToken);
        if (empresa.Current is not Guid empresaId) throw new UnauthorizedAccessException("Selecciona una empresa.");
        var opera = await (from a in organizacion.SucursalDepartamentos.AsNoTracking()
            join d in organizacion.Departamentos.AsNoTracking() on a.DepartamentoId equals d.Id
            join s in organizacion.Sucursales.AsNoTracking() on a.SucursalId equals s.Id
            where a.SucursalId == request.SucursalId && d.Id == request.DepartamentoId && a.Estatus == EstatusCatalogo.Activo
                && d.Estatus == EstatusCatalogo.Activo && s.Estatus == EstatusCatalogo.Activo
            select a.Id).AnyAsync(cancellationToken);
        if (!opera) throw new BusinessRuleException("CECO_DEPARTAMENTO_INVALIDO", "El departamento no opera en esta sucursal o está inactivo.");
        // Buscar está paginado; validar el nodo específico sin depender del top-N.
        var nodo = (await Infrastructure.PublicAdapters.CentroCostoCatalogoLectura.ObtenerAsync(db, cancellationToken)).SingleOrDefault(x => x.Id == request.CentroCostoId);
        if (nodo is null || !nodo.Activo || nodo.Nivel == 3)
            throw new BusinessRuleException("CECO_EQUIVALENCIA_INVALIDA", "La equivalencia requiere una planta o un área activa (Dim1/Dim2); la máquina es opcional en Compras.");
        var eq = await db.DepartamentoCentrosCosto.SingleOrDefaultAsync(x => x.SucursalId == request.SucursalId && x.DepartamentoId == request.DepartamentoId, cancellationToken);
        if ((eq?.Version ?? 0) != request.VersionEsperada) throw new ConcurrencyException(nameof(DepartamentoCentroCosto), eq?.Id ?? request.DepartamentoId);
        if (eq is null) db.DepartamentoCentrosCosto.Add(new DepartamentoCentroCosto(Guid.CreateVersion7(), empresaId,
            request.SucursalId, request.DepartamentoId, request.CentroCostoId, request.Observaciones));
        else eq.Cambiar(request.CentroCostoId, request.Observaciones);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
