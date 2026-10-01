using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application.Events;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Usuarios;

// Permisos personalizados por usuario (ADR-0053):
//   efectivos = (permisos del rol ∪ Conceder) \ Denegar, por (usuario, empresa).

/// <summary>Origen de un permiso en la vista de permisos efectivos.</summary>
public static class OrigenPermiso
{
    /// <summary>Lo trae el rol y no fue denegado (efectivo).</summary>
    public const string Rol = "Rol";

    /// <summary>Lo añade una excepción Conceder (efectivo).</summary>
    public const string Concedido = "Concedido";

    /// <summary>Una excepción Denegar lo quita (NO efectivo).</summary>
    public const string Denegado = "Denegado";
}

/// <summary>Permiso con su origen. Solo se listan Rol, Concedido y Denegado.</summary>
public sealed record PermisoEfectivoItem(
    Guid PermisoId,
    string Codigo,
    string Origen,
    bool Efectivo,
    string? Motivo);

public sealed record PermisosEfectivosUsuarioResponse(
    Guid UsuarioId,
    Guid EmpresaId,
    Guid? RolId,
    string? RolCodigo,
    bool RolEsSuperAdmin,
    IReadOnlyList<PermisoEfectivoItem> Permisos,
    int Concedidos,
    int Denegados);

public sealed record PermisoOverrideItem(Guid PermisoId, string Efecto, string? Motivo);

public sealed record PermisosOverrideResumenResponse(
    Guid UsuarioId, Guid EmpresaId, int Concedidos, int Denegados);

// ---------------------------------------------------------------------------
// Reglas compartidas (sección 6 del plan).
// ---------------------------------------------------------------------------
internal static class PermisosOverrideReglas
{
    public static Guid EnsureCallerNoEsElMismo(Guid? callerId, Guid usuarioId)
    {
        var caller = callerId
            ?? throw new ForbiddenException("NO_AUTENTICADO", "Se requiere un usuario autenticado.");
        if (caller == usuarioId)
        {
            throw new ForbiddenException(
                "PERMISOS_OVERRIDE_AUTO_EDICION",
                "Nadie puede editar sus propios permisos personalizados.");
        }
        return caller;
    }

    /// <summary>
    /// Devuelve los permisos efectivos del caller en la empresa objetivo y
    /// exige que tenga <c>identidad.usuarios.gestionar-permisos</c> en ella.
    /// </summary>
    public static async Task<IReadOnlyCollection<string>> PermisosDelCallerAsync(
        IPermissionLoader loader, Guid callerId, Guid empresaId, CancellationToken ct)
    {
        var permisos = await loader.LoadForUserInEmpresaAsync(callerId, empresaId, ct);
        if (!permisos.Contains(PermisosCanonicos.IdentidadUsuariosGestionarPermisos))
        {
            throw new ForbiddenException(
                "PERMISOS_OVERRIDE_SIN_ALCANCE_EMPRESA",
                "No tiene permiso para gestionar permisos de usuarios en esa empresa.");
        }
        return permisos;
    }

    public static async Task<Usuario> ObtenerUsuarioAsync(
        IdentidadDbContext db, Guid usuarioId, CancellationToken ct) =>
        await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId, ct)
        ?? throw new EntityNotFoundException(
            "USUARIO_NO_ENCONTRADO", $"No existe usuario con id '{usuarioId}'.");

    /// <summary>
    /// Cambio de rol (asignar uno nuevo o revocar): las excepciones del
    /// <c>(usuario, empresa)</c> se borran en la misma transacción (el caller
    /// hace <c>SaveChanges</c>). Devuelve cuántas se marcaron para borrar.
    /// </summary>
    public static async Task<int> QuitarPorCambioDeRolAsync(
        IdentidadDbContext db, Guid usuarioId, Guid empresaId, CancellationToken ct)
    {
        var existentes = await db.UsuarioPermisoOverrides
            .Where(o => o.UsuarioId == usuarioId && o.EmpresaId == empresaId)
            .ToListAsync(ct);
        db.UsuarioPermisoOverrides.RemoveRange(existentes);
        return existentes.Count;
    }

    /// <summary>Invalida caché y publica el evento solo si se borró algo.</summary>
    public static async Task NotificarCambioDeRolAsync(
        IPermissionCache cache, IIntegrationEventPublisher events, IClock clock,
        Guid usuarioId, Guid empresaId, int borrados, CancellationToken ct)
    {
        await cache.InvalidateAsync(usuarioId, empresaId, ct);
        if (borrados > 0)
        {
            await events.PublishAsync(
                new UsuarioPermisosOverrideActualizadosEvent(usuarioId, empresaId, 0, 0, clock.UtcNow),
                ct);
        }
    }

    public static bool TryParseEfecto(string? valor, out EfectoPermiso efecto) =>
        Enum.TryParse(valor, ignoreCase: true, out efecto)
        && (efecto == EfectoPermiso.Conceder || efecto == EfectoPermiso.Denegar)
        && !int.TryParse(valor, out _);
}

// ---------------------------------------------------------------------------
// GET permisos efectivos con origen.
// ---------------------------------------------------------------------------
public sealed record ObtenerPermisosEfectivosUsuarioQuery(Guid UsuarioId, Guid EmpresaId)
    : IRequest<PermisosEfectivosUsuarioResponse>;

public sealed class ObtenerPermisosEfectivosUsuarioValidator
    : AbstractValidator<ObtenerPermisosEfectivosUsuarioQuery>
{
    public ObtenerPermisosEfectivosUsuarioValidator()
    {
        RuleFor(q => q.UsuarioId).NotEmpty();
        RuleFor(q => q.EmpresaId).NotEmpty();
    }
}

public sealed class ObtenerPermisosEfectivosUsuarioHandler
    : IRequestHandler<ObtenerPermisosEfectivosUsuarioQuery, PermisosEfectivosUsuarioResponse>
{
    private readonly IdentidadDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public ObtenerPermisosEfectivosUsuarioHandler(
        IdentidadDbContext db, ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<PermisosEfectivosUsuarioResponse> Handle(
        ObtenerPermisosEfectivosUsuarioQuery query, CancellationToken cancellationToken)
    {
        // Consulta admin cross-empresa (gate: identidad.usuarios.leer).
        using var bypass = _empresaContext.Bypass();

        await PermisosOverrideReglas.ObtenerUsuarioAsync(_db, query.UsuarioId, cancellationToken);

        var roles = await (
            from uer in _db.UsuarioEmpresaRoles.AsNoTracking()
            where uer.UsuarioId == query.UsuarioId && uer.EmpresaId == query.EmpresaId
            join r in _db.Roles.AsNoTracking() on uer.RolId equals r.Id
            where r.Activo
            select new { r.Id, r.Codigo })
            .ToListAsync(cancellationToken);

        var rolIds = roles.Select(r => r.Id).ToList();
        var delRol = await (
            from rp in _db.RolPermisos.AsNoTracking()
            where rolIds.Contains(rp.RolId)
            join p in _db.Permisos.AsNoTracking() on rp.PermisoId equals p.Id
            select new { p.Id, p.Codigo })
            .Distinct()
            .ToListAsync(cancellationToken);

        var overrides = await (
            from o in _db.UsuarioPermisoOverrides.AsNoTracking()
            where o.UsuarioId == query.UsuarioId && o.EmpresaId == query.EmpresaId
            join p in _db.Permisos.AsNoTracking() on o.PermisoId equals p.Id
            select new { p.Id, p.Codigo, o.Efecto, o.Motivo })
            .ToListAsync(cancellationToken);

        var items = new Dictionary<Guid, PermisoEfectivoItem>();
        // Sin rol activo en la empresa no hay base: el loader tampoco concede.
        if (roles.Count > 0)
        {
            foreach (var p in delRol)
                items[p.Id] = new PermisoEfectivoItem(p.Id, p.Codigo, OrigenPermiso.Rol, true, null);

            foreach (var o in overrides)
            {
                items[o.Id] = o.Efecto == EfectoPermiso.Conceder
                    ? new PermisoEfectivoItem(o.Id, o.Codigo, OrigenPermiso.Concedido, true, o.Motivo)
                    : new PermisoEfectivoItem(o.Id, o.Codigo, OrigenPermiso.Denegado, false, o.Motivo);
            }
        }

        var lista = items.Values.OrderBy(i => i.Codigo, StringComparer.Ordinal).ToList();
        var primero = roles.OrderBy(r => r.Codigo, StringComparer.Ordinal).FirstOrDefault();
        return new PermisosEfectivosUsuarioResponse(
            query.UsuarioId,
            query.EmpresaId,
            primero?.Id,
            primero?.Codigo,
            roles.Any(r => r.Codigo == RevocarRolDeUsuarioHandler.SuperAdminCodigo),
            lista,
            lista.Count(i => i.Origen == OrigenPermiso.Concedido),
            lista.Count(i => i.Origen == OrigenPermiso.Denegado));
    }
}

// ---------------------------------------------------------------------------
// PUT batch: reemplaza atómicamente la lista de excepciones.
// ---------------------------------------------------------------------------

/// <summary>
/// Reemplaza la lista de excepciones del usuario en la empresa.
/// <list type="bullet">
///   <item>400 validación: duplicados (incluye mismo permiso en ambos efectos), efecto inválido.</item>
///   <item>403 <c>PERMISOS_OVERRIDE_AUTO_EDICION</c>, <c>PERMISOS_OVERRIDE_SIN_ALCANCE_EMPRESA</c>,
///         <c>PERMISOS_OVERRIDE_ESCALADA</c> (conceder un permiso que el caller no posee).</item>
///   <item>404 usuario o permiso inexistente.</item>
///   <item>422 usuario inactivo, sin rol en la empresa o super-admin (protegido).</item>
/// </list>
/// </summary>
public sealed record ActualizarPermisosOverrideUsuarioCommand(
    Guid UsuarioId,
    Guid EmpresaId,
    IReadOnlyList<PermisoOverrideItem> Overrides) : IRequest<PermisosOverrideResumenResponse>;

public sealed class ActualizarPermisosOverrideUsuarioValidator
    : AbstractValidator<ActualizarPermisosOverrideUsuarioCommand>
{
    public ActualizarPermisosOverrideUsuarioValidator()
    {
        RuleFor(c => c.UsuarioId).NotEmpty();
        RuleFor(c => c.EmpresaId).NotEmpty();
        RuleFor(c => c.Overrides).NotNull();
        RuleForEach(c => c.Overrides).ChildRules(o =>
        {
            o.RuleFor(x => x.PermisoId).NotEmpty();
            o.RuleFor(x => x.Efecto)
                .Must(e => PermisosOverrideReglas.TryParseEfecto(e, out _))
                .WithMessage("Efecto inválido: use 'Conceder' o 'Denegar'.");
            o.RuleFor(x => x.Motivo).MaximumLength(UsuarioPermisoOverride.MotivoMaxLength);
        });
        RuleFor(c => c.Overrides)
            .Must(l => l.Select(o => o.PermisoId).Distinct().Count() == l.Count)
            .When(c => c.Overrides is not null)
            .WithMessage("Un permiso no puede repetirse ni aparecer con ambos efectos.");
    }
}

public sealed class ActualizarPermisosOverrideUsuarioHandler
    : IRequestHandler<ActualizarPermisosOverrideUsuarioCommand, PermisosOverrideResumenResponse>
{
    private readonly IdentidadDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly IPermissionLoader _permissionLoader;
    private readonly IPermissionCache _permissionCache;
    private readonly IClock _clock;

    public ActualizarPermisosOverrideUsuarioHandler(
        IdentidadDbContext db,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext empresaContext,
        IPermissionLoader permissionLoader,
        IPermissionCache permissionCache,
        IClock clock)
    {
        _db = db;
        _events = events;
        _currentUser = currentUser;
        _empresaContext = empresaContext;
        _permissionLoader = permissionLoader;
        _permissionCache = permissionCache;
        _clock = clock;
    }

    public async Task<PermisosOverrideResumenResponse> Handle(
        ActualizarPermisosOverrideUsuarioCommand command, CancellationToken cancellationToken)
    {
        // Operación cross-empresa por diseño (gate RBAC); mismo patrón que
        // AsignarRolAUsuarioHandler.
        using var bypass = _empresaContext.Bypass();

        var callerId = PermisosOverrideReglas.EnsureCallerNoEsElMismo(
            _currentUser.UserId, command.UsuarioId);

        var usuario = await PermisosOverrideReglas.ObtenerUsuarioAsync(
            _db, command.UsuarioId, cancellationToken);
        if (!usuario.Activo)
        {
            throw new BusinessRuleException(
                "USUARIO_INACTIVO", "No se pueden editar permisos de un usuario inactivo.");
        }

        var roles = await (
            from uer in _db.UsuarioEmpresaRoles.AsNoTracking()
            where uer.UsuarioId == command.UsuarioId && uer.EmpresaId == command.EmpresaId
            join r in _db.Roles.AsNoTracking() on uer.RolId equals r.Id
            where r.Activo
            select r.Codigo)
            .ToListAsync(cancellationToken);
        if (roles.Count == 0)
        {
            throw new BusinessRuleException(
                "USUARIO_SIN_ROL_EN_EMPRESA",
                "El usuario no tiene un rol activo en la empresa indicada.");
        }
        if (roles.Contains(RevocarRolDeUsuarioHandler.SuperAdminCodigo))
        {
            throw new BusinessRuleException(
                "SUPER_ADMIN_PERMISOS_PROTEGIDOS",
                "Los permisos del super-admin se gestionan via bootstrap; no por API.");
        }

        var callerPermisos = await PermisosOverrideReglas.PermisosDelCallerAsync(
            _permissionLoader, callerId, command.EmpresaId, cancellationToken);

        var deseados = command.Overrides
            .Select(o => (o.PermisoId, Efecto: Enum.Parse<EfectoPermiso>(o.Efecto, ignoreCase: true), o.Motivo))
            .ToList();

        var ids = deseados.Select(d => d.PermisoId).ToList();
        var catalogo = await _db.Permisos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Codigo, cancellationToken);

        var faltantes = ids.Where(id => !catalogo.ContainsKey(id)).ToList();
        if (faltantes.Count > 0)
        {
            throw new EntityNotFoundException(
                "PERMISO_NO_ENCONTRADO",
                $"Los siguientes permisos no existen en el catálogo: {string.Join(", ", faltantes)}.");
        }

        // Sin escalada: solo se concede lo que el caller posee en esa empresa.
        var escalados = deseados
            .Where(d => d.Efecto == EfectoPermiso.Conceder
                        && !callerPermisos.Contains(catalogo[d.PermisoId]))
            .Select(d => catalogo[d.PermisoId])
            .ToList();
        if (escalados.Count > 0)
        {
            throw new ForbiddenException(
                "PERMISOS_OVERRIDE_ESCALADA",
                $"No puede conceder permisos que usted no posee: {string.Join(", ", escalados)}.");
        }

        var actuales = await _db.UsuarioPermisoOverrides
            .Where(o => o.UsuarioId == command.UsuarioId && o.EmpresaId == command.EmpresaId)
            .ToListAsync(cancellationToken);

        var deseadosIds = deseados.Select(d => d.PermisoId).ToHashSet();
        _db.UsuarioPermisoOverrides.RemoveRange(
            actuales.Where(a => !deseadosIds.Contains(a.PermisoId)));

        foreach (var d in deseados)
        {
            var existente = actuales.FirstOrDefault(a => a.PermisoId == d.PermisoId);
            if (existente is null)
            {
                _db.UsuarioPermisoOverrides.Add(new UsuarioPermisoOverride(
                    Guid.CreateVersion7(), command.UsuarioId, command.EmpresaId,
                    d.PermisoId, d.Efecto, d.Motivo, callerId));
            }
            else
            {
                existente.Actualizar(d.Efecto, d.Motivo, callerId);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _permissionCache.InvalidateAsync(command.UsuarioId, command.EmpresaId, cancellationToken);

        var concedidos = deseados.Count(d => d.Efecto == EfectoPermiso.Conceder);
        var denegados = deseados.Count - concedidos;

        // PLATFORM-TODO(<AdminOutbox>): mismo caveat que UsuarioRolAsignadoEvent.
        await _events.PublishAsync(
            new UsuarioPermisosOverrideActualizadosEvent(
                command.UsuarioId, command.EmpresaId, concedidos, denegados, _clock.UtcNow),
            cancellationToken);

        return new PermisosOverrideResumenResponse(
            command.UsuarioId, command.EmpresaId, concedidos, denegados);
    }
}

// ---------------------------------------------------------------------------
// DELETE: vuelve al rol (quita todas las excepciones). Idempotente.
// ---------------------------------------------------------------------------
public sealed record RestablecerPermisosOverrideUsuarioCommand(Guid UsuarioId, Guid EmpresaId)
    : IRequest;

public sealed class RestablecerPermisosOverrideUsuarioValidator
    : AbstractValidator<RestablecerPermisosOverrideUsuarioCommand>
{
    public RestablecerPermisosOverrideUsuarioValidator()
    {
        RuleFor(c => c.UsuarioId).NotEmpty();
        RuleFor(c => c.EmpresaId).NotEmpty();
    }
}

public sealed class RestablecerPermisosOverrideUsuarioHandler
    : IRequestHandler<RestablecerPermisosOverrideUsuarioCommand>
{
    private readonly IdentidadDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly IPermissionLoader _permissionLoader;
    private readonly IPermissionCache _permissionCache;
    private readonly IClock _clock;

    public RestablecerPermisosOverrideUsuarioHandler(
        IdentidadDbContext db,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext empresaContext,
        IPermissionLoader permissionLoader,
        IPermissionCache permissionCache,
        IClock clock)
    {
        _db = db;
        _events = events;
        _currentUser = currentUser;
        _empresaContext = empresaContext;
        _permissionLoader = permissionLoader;
        _permissionCache = permissionCache;
        _clock = clock;
    }

    public async Task Handle(
        RestablecerPermisosOverrideUsuarioCommand command, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        var callerId = PermisosOverrideReglas.EnsureCallerNoEsElMismo(
            _currentUser.UserId, command.UsuarioId);
        await PermisosOverrideReglas.ObtenerUsuarioAsync(_db, command.UsuarioId, cancellationToken);
        await PermisosOverrideReglas.PermisosDelCallerAsync(
            _permissionLoader, callerId, command.EmpresaId, cancellationToken);

        var actuales = await _db.UsuarioPermisoOverrides
            .Where(o => o.UsuarioId == command.UsuarioId && o.EmpresaId == command.EmpresaId)
            .ToListAsync(cancellationToken);
        if (actuales.Count == 0)
        {
            return;
        }

        _db.UsuarioPermisoOverrides.RemoveRange(actuales);
        await _db.SaveChangesAsync(cancellationToken);
        await _permissionCache.InvalidateAsync(command.UsuarioId, command.EmpresaId, cancellationToken);

        await _events.PublishAsync(
            new UsuarioPermisosOverrideActualizadosEvent(
                command.UsuarioId, command.EmpresaId, 0, 0, _clock.UtcNow),
            cancellationToken);
    }
}
