using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Identidad.Application.Events;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Asigna un rol a un usuario en el contexto de una empresa
/// (F-Admin-PR4.2). Usa la factory <c>Usuario.AsignarRolEnEmpresa</c>.
///
/// <list type="bullet">
///   <item>404 si Usuario/Empresa/Rol no existen
///         (<c>USUARIO_NO_ENCONTRADO</c>, <c>EMPRESA_NO_ENCONTRADA</c>,
///         <c>ROL_NO_ENCONTRADO</c>).</item>
///   <item>409 <c>USUARIO_ASIGNACION_DUPLICADA</c> si ya existe la combinación
///         <c>(UsuarioId, EmpresaId, RolId)</c> (unique idx en BD).</item>
///   <item>Publica <see cref="UsuarioRolAsignadoEvent"/> via
///         <see cref="IIntegrationEventPublisher"/>.</item>
/// </list>
/// </summary>
public sealed record AsignarRolAUsuarioCommand(
    Guid UsuarioId,
    Guid EmpresaId,
    Guid RolId) : IRequest<UsuarioEmpresaRolResponse>;

public sealed class AsignarRolAUsuarioValidator
    : AbstractValidator<AsignarRolAUsuarioCommand>
{
    public AsignarRolAUsuarioValidator()
    {
        RuleFor(c => c.UsuarioId).NotEmpty();
        RuleFor(c => c.EmpresaId).NotEmpty();
        RuleFor(c => c.RolId).NotEmpty();
    }
}

public sealed class AsignarRolAUsuarioHandler
    : IRequestHandler<AsignarRolAUsuarioCommand, UsuarioEmpresaRolResponse>
{
    private readonly IdentidadDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly IClock _clock;

    public AsignarRolAUsuarioHandler(
        IdentidadDbContext db,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext empresaContext,
        IClock clock)
    {
        _db = db;
        _events = events;
        _currentUser = currentUser;
        _empresaContext = empresaContext;
        _clock = clock;
    }

    public async Task<UsuarioEmpresaRolResponse> Handle(
        AsignarRolAUsuarioCommand command, CancellationToken cancellationToken)
    {
        // Asignar usuarios a empresas es por definición una operación
        // cross-empresa: el endpoint está gated por
        // identidad.asignaciones.administrar (super-admins), que pueden
        // asignar usuarios a CUALQUIER empresa, no solo a la actual.
        // UsuarioEmpresaRol implementa IPerteneceAEmpresa para que el
        // global query filter aplique en consultas normales, pero al
        // CREAR esta entidad el EmpresaContextSaveChangesInterceptor
        // bloquea con CROSS_EMPRESA_VIOLATION si no bypaseamos.
        using var bypass = _empresaContext.Bypass();

        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Id == command.UsuarioId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_NO_ENCONTRADO",
                $"No existe usuario con id '{command.UsuarioId}'.");

        var empresaExiste = await _db.Set<Empresa>().AsNoTracking()
            .AnyAsync(e => e.Id == command.EmpresaId, cancellationToken);
        if (!empresaExiste)
        {
            throw new EntityNotFoundException(
                "EMPRESA_NO_ENCONTRADA",
                $"No existe empresa con id '{command.EmpresaId}'.");
        }

        var rolExiste = await _db.Roles.AsNoTracking()
            .AnyAsync(r => r.Id == command.RolId, cancellationToken);
        if (!rolExiste)
        {
            throw new EntityNotFoundException(
                "ROL_NO_ENCONTRADO",
                $"No existe rol con id '{command.RolId}'.");
        }

        var duplicada = await _db.UsuarioEmpresaRoles.AsNoTracking()
            .AnyAsync(
                uer => uer.UsuarioId == command.UsuarioId
                       && uer.EmpresaId == command.EmpresaId
                       && uer.RolId == command.RolId,
                cancellationToken);
        if (duplicada)
        {
            throw new ConflictException(
                "USUARIO_ASIGNACION_DUPLICADA",
                $"El usuario ya tiene asignado ese rol en la empresa indicada.");
        }

        var asignacion = usuario.AsignarRolEnEmpresa(
            command.EmpresaId,
            command.RolId,
            _currentUser.UserId);

        _db.UsuarioEmpresaRoles.Add(asignacion);
        await _db.SaveChangesAsync(cancellationToken);

        // PLATFORM-TODO(<AdminOutbox>): IdentidadDbContext no tiene el
        // OutboxSaveChangesInterceptor wireado. El evento se encola al
        // buffer scoped y se pierde al cerrar el scope. Aceptable en MVP —
        // ningún consumer activo. Mismo patrón que F-Admin-PR3.2.
        await _events.PublishAsync(
            new UsuarioRolAsignadoEvent(
                command.UsuarioId,
                command.EmpresaId,
                command.RolId,
                _clock.UtcNow),
            cancellationToken);

        return new UsuarioEmpresaRolResponse(
            asignacion.Id,
            asignacion.UsuarioId,
            asignacion.EmpresaId,
            asignacion.RolId,
            asignacion.CreatedAt);
    }
}
