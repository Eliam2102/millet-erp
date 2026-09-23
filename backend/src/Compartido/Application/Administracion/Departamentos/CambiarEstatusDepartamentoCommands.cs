using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Departamentos;

public sealed record DesactivarDepartamentoCommand(Guid Id) : IRequest<DepartamentoResponse>;

public sealed record ReactivarDepartamentoCommand(Guid Id) : IRequest<DepartamentoResponse>;

public sealed class DesactivarDepartamentoHandler
    : IRequestHandler<DesactivarDepartamentoCommand, DepartamentoResponse>
{
    private readonly CompartidoDbContext _db;

    public DesactivarDepartamentoHandler(CompartidoDbContext db) => _db = db;

    public async Task<DepartamentoResponse> Handle(
        DesactivarDepartamentoCommand command,
        CancellationToken cancellationToken)
    {
        var departamento = await _db.Departamentos
            .FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "DEPARTAMENTO_NO_ENCONTRADO",
                $"No existe departamento con id '{command.Id}'.");

        if (departamento.Estatus != EstatusCatalogo.Inactivo)
        {
            departamento.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(departamento);
    }

    private static DepartamentoResponse ToResponse(Departamento departamento) =>
        new(departamento.Id, departamento.Clave, departamento.Nombre,
            departamento.Estatus, departamento.Version);
}

public sealed class ReactivarDepartamentoHandler
    : IRequestHandler<ReactivarDepartamentoCommand, DepartamentoResponse>
{
    private readonly CompartidoDbContext _db;

    public ReactivarDepartamentoHandler(CompartidoDbContext db) => _db = db;

    public async Task<DepartamentoResponse> Handle(
        ReactivarDepartamentoCommand command,
        CancellationToken cancellationToken)
    {
        var departamento = await _db.Departamentos
            .FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "DEPARTAMENTO_NO_ENCONTRADO",
                $"No existe departamento con id '{command.Id}'.");

        if (departamento.Estatus != EstatusCatalogo.Activo)
        {
            departamento.Activar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new DepartamentoResponse(
            departamento.Id, departamento.Clave, departamento.Nombre,
            departamento.Estatus, departamento.Version);
    }
}
