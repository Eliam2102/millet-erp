using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Convierte una cuenta técnica en cuenta de persona (86e3yxe9m): quita
/// <c>EsCuentaTecnica</c>. Solo un super-admin puede hacerlo, con motivo
/// (mínimo 15 caracteres) que queda en la bitácora de auditoría.
///
/// <para>
/// Candado del último super-admin: una cuenta de persona sin empleado se
/// queda sin acceso cuando <c>Identidad:ExigirEmpleadoParaAcceso</c> está
/// encendida. Por eso, si la cuenta es super-admin y no tiene empleado, debe
/// quedar al menos otro super-admin activo después de la operación (422
/// <c>USUARIO_ULTIMO_SUPER_ADMIN</c>).
/// </para>
/// </summary>
public sealed record ConvertirEnCuentaDePersonaCommand(Guid UsuarioId, int VersionEsperada, string Motivo)
    : IRequest<UsuarioResponse>;

public sealed class ConvertirEnCuentaDePersonaValidator : AbstractValidator<ConvertirEnCuentaDePersonaCommand>
{
    public const int MotivoMinimo = 15;
    public const int MotivoMaximo = 500;

    public ConvertirEnCuentaDePersonaValidator()
    {
        RuleFor(c => c.UsuarioId).NotEmpty();
        RuleFor(c => c.Motivo)
            .Must(m => (m?.Trim().Length ?? 0) >= MotivoMinimo)
            .WithMessage($"El motivo debe tener al menos {MotivoMinimo} caracteres.")
            .MaximumLength(MotivoMaximo);
    }
}

public sealed class ConvertirEnCuentaDePersonaHandler
    : IRequestHandler<ConvertirEnCuentaDePersonaCommand, UsuarioResponse>
{
    private readonly IdentidadDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAuditLogWriter _audit;
    private readonly IPermissionCache _permissionCache;
    private readonly CoreDbContext _core;

    public ConvertirEnCuentaDePersonaHandler(
        IdentidadDbContext db,
        CompartidoDbContext compartido,
        CoreDbContext core,
        ICurrentUserContext currentUser,
        IAuditLogWriter audit,
        IPermissionCache permissionCache)
    {
        _db = db;
        _compartido = compartido;
        _currentUser = currentUser;
        _audit = audit;
        _permissionCache = permissionCache;
        _core = core;
    }

    public async Task<UsuarioResponse> Handle(
        ConvertirEnCuentaDePersonaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var superAdminRolId = await _db.Roles.AsNoTracking()
            .Where(r => r.Codigo == DesactivarUsuarioHandler.SuperAdminCodigo)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        // Solo un super-admin convierte cuentas técnicas.
        var actorId = _currentUser.UserId;
        var actorEsSuperAdmin = superAdminRolId is Guid rolActor && actorId is Guid actor
            && await _db.UsuarioEmpresaRoles.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(uer => uer.UsuarioId == actor && uer.RolId == rolActor, ct);
        if (!actorEsSuperAdmin)
            throw new ForbiddenException(
                "USUARIO_CONVERTIR_SOLO_SUPER_ADMIN",
                "Solo un super administrador puede convertir una cuenta técnica en cuenta de persona.");

        var usuario = await _db.Usuarios.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == command.UsuarioId, ct)
            ?? throw new EntityNotFoundException(
                "USUARIO_NO_ENCONTRADO", $"No existe usuario con id '{command.UsuarioId}'.");

        if (usuario.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Domain.Usuario), usuario.Id);

        if (!usuario.EsCuentaTecnica)
            throw new BusinessRuleException(
                "USUARIO_NO_ES_CUENTA_TECNICA", "La cuenta ya es una cuenta de persona.");

        // Mismo criterio que el login con ExigirEmpleadoParaAcceso: empleado activo.
        var tieneEmpleado = await _compartido.Empleados.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(e => e.UsuarioId == usuario.Id && e.Estatus == Millet.Catalogos.Domain.EstatusCatalogo.Activo, ct);

        if (superAdminRolId is Guid rolSuper && !tieneEmpleado)
        {
            var esSuperAdmin = await _db.UsuarioEmpresaRoles.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(uer => uer.UsuarioId == usuario.Id && uer.RolId == rolSuper, ct);
            if (esSuperAdmin)
            {
                var otroSuperAdminActivo = await (
                    from uer in _db.UsuarioEmpresaRoles.IgnoreQueryFilters().AsNoTracking()
                    join u in _db.Usuarios.IgnoreQueryFilters().AsNoTracking() on uer.UsuarioId equals u.Id
                    where uer.RolId == rolSuper && u.Activo && u.Id != usuario.Id
                    select u.Id).AnyAsync(ct);
                if (!otroSuperAdminActivo)
                    throw new BusinessRuleException(
                        "USUARIO_ULTIMO_SUPER_ADMIN",
                        "No se puede convertir al único super administrador activo: sin empleado ligado podría " +
                        "quedarse sin acceso. Asigne otro super administrador antes de convertir esta cuenta.");
            }
        }

        var motivo = command.Motivo.Trim();
        usuario.DesmarcarCuentaTecnica();

        // Conversión y bitácora son atómicas (86e3yxe9m): la bitácora (core) se une a la conexión y la
        // transacción de Identidad; si cualquiera falla, la cuenta sigue siendo técnica. La bitácora va
        // primero para que, sin base relacional (pruebas unitarias), una falla tampoco convierta la cuenta.
        await EnTransaccionAsync(async () =>
        {
            await _audit.RegistrarAsync(
                // core.audit_log.operacion es varchar(32).
                operacion: "usuario.convertido-a-persona",
                modulo: "Identidad",
                entidad: "Usuario",
                entidadId: usuario.Id,
                aggregateRootId: usuario.Id,
                actorNombre: _currentUser.UserName ?? "Usuario",
                actorTipo: "usuario",
                actorEmail: _currentUser.Email,
                entidadEtiqueta: usuario.Email,
                resumen: $"Cuenta técnica convertida en cuenta de persona. Motivo: {motivo}",
                usuarioId: actorId,
                cambios: System.Text.Json.JsonSerializer.Serialize(new
                {
                    esCuentaTecnica = new { antes = true, despues = false },
                    motivo,
                }),
                cancellationToken: ct);
            await _db.SaveChangesAsync(ct);
        }, ct);
        await _permissionCache.InvalidateAllForUserAsync(usuario.Id, ct);

        return new UsuarioResponse(
            usuario.Id, usuario.Email, usuario.EntraOid, usuario.Nombre, usuario.DepartamentoId,
            usuario.Activo, usuario.Version, EsCuentaTecnica: usuario.EsCuentaTecnica,
            PrimerAccesoEn: usuario.PrimerAccesoEn);
    }

    private async Task EnTransaccionAsync(Func<Task> operacion, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            await operacion();
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Core no debe tener su propia conexión abierta al unirse (mismo patrón que TransaccionColaborador).
        await _core.Database.CloseConnectionAsync();
        _core.Database.SetDbConnection(_db.Database.GetDbConnection());
        await _core.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
        try
        {
            await operacion();
            await tx.CommitAsync(ct);
        }
        finally
        {
            // Sin commit, el dispose de tx hace rollback.
            await _core.Database.UseTransactionAsync(null, CancellationToken.None);
        }
    }
}
