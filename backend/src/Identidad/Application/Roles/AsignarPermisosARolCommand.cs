using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application.Events;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Roles;

/// <summary>
/// Batch atómico: reemplaza la matriz de permisos asignada a un rol con
/// la lista <see cref="PermisoIds"/>. Calcula el diff
/// (existentes vs deseados), agrega los faltantes y borra los extras
/// en un solo <c>SaveChangesAsync</c> (F-Admin-PR3.2).
///
/// <list type="bullet">
///   <item>404 si el rol no existe.</item>
///   <item>422 <c>ROL_DEL_SISTEMA_NO_EDITABLE</c> si <c>EsDelSistema</c>.</item>
///   <item>404 <c>PERMISO_NO_ENCONTRADO</c> si alguno de los
///         <see cref="PermisoIds"/> no existe en <c>identidad.permisos</c>.</item>
///   <item>Publica <see cref="RolPermisosActualizadosEvent"/> via
///         <see cref="IIntegrationEventPublisher"/>.</item>
/// </list>
/// </summary>
public sealed record AsignarPermisosARolCommand(
    Guid RolId,
    IReadOnlyList<Guid> PermisoIds) : IRequest<RolResponse>;

public sealed class AsignarPermisosARolValidator
    : AbstractValidator<AsignarPermisosARolCommand>
{
    public AsignarPermisosARolValidator()
    {
        RuleFor(c => c.RolId).NotEmpty();
        RuleFor(c => c.PermisoIds).NotNull();
        RuleForEach(c => c.PermisoIds).NotEmpty();
    }
}

public sealed class AsignarPermisosARolHandler
    : IRequestHandler<AsignarPermisosARolCommand, RolResponse>
{
    private readonly IdentidadDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly IClock _clock;

    public AsignarPermisosARolHandler(
        IdentidadDbContext db,
        IIntegrationEventPublisher events,
        IClock clock)
    {
        _db = db;
        _events = events;
        _clock = clock;
    }

    public async Task<RolResponse> Handle(
        AsignarPermisosARolCommand command, CancellationToken cancellationToken)
    {
        var rol = await _db.Roles
            .FirstOrDefaultAsync(r => r.Id == command.RolId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ROL_NO_ENCONTRADO",
                $"No existe rol con id '{command.RolId}'.");

        if (rol.EsDelSistema)
        {
            throw new BusinessRuleException(
                "ROL_DEL_SISTEMA_NO_EDITABLE",
                "La matriz de permisos del super-admin se gestiona via bootstrap; no por API.");
        }

        var deseados = command.PermisoIds.Distinct().ToHashSet();

        // Validar que todos los permisos existen.
        if (deseados.Count > 0)
        {
            var existentesEnCatalogo = await _db.Permisos.AsNoTracking()
                .Where(p => deseados.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            var faltantes = deseados.Except(existentesEnCatalogo).ToList();
            if (faltantes.Count > 0)
            {
                throw new EntityNotFoundException(
                    "PERMISO_NO_ENCONTRADO",
                    $"Los siguientes permisos no existen en el catálogo: {string.Join(", ", faltantes)}.");
            }
        }

        // Computar diff vs los actuales del rol.
        var actuales = await _db.RolPermisos
            .Where(rp => rp.RolId == command.RolId)
            .ToListAsync(cancellationToken);
        var actualesIds = actuales.Select(rp => rp.PermisoId).ToHashSet();

        // Borrar los extras.
        var aRemover = actuales
            .Where(rp => !deseados.Contains(rp.PermisoId))
            .ToList();
        if (aRemover.Count > 0)
        {
            _db.RolPermisos.RemoveRange(aRemover);
        }

        // Agregar los nuevos.
        var aAgregar = deseados
            .Where(pid => !actualesIds.Contains(pid))
            .Select(pid => new RolPermiso(Guid.CreateVersion7(), command.RolId, pid))
            .ToList();
        if (aAgregar.Count > 0)
        {
            await _db.RolPermisos.AddRangeAsync(aAgregar, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // PLATFORM-TODO(<AdminOutbox>): IdentidadDbContext no tiene el
        // OutboxSaveChangesInterceptor wireado. El evento se encola al
        // buffer scoped y se pierde al cerrar el scope. Aceptable en MVP —
        // ningún consumer activo. Mismo patrón que F-Admin-PR2.3.
        await _events.PublishAsync(
            new RolPermisosActualizadosEvent(
                command.RolId,
                deseados.ToList(),
                _clock.UtcNow),
            cancellationToken);

        return new RolResponse(
            rol.Id, rol.Codigo, rol.Nombre, rol.Descripcion,
            rol.EsDelSistema, rol.Activo, rol.Version);
    }
}
