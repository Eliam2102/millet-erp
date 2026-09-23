using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Crea un usuario nuevo en <c>identidad.usuarios</c> (F-Admin-PR4.2).
///
/// <list type="bullet">
///   <item><see cref="Id"/> = <see cref="Guid.Empty"/> ⇒ se genera con
///         <c>Guid.CreateVersion7()</c>. Tests deterministas pueden pasar
///         un id concreto.</item>
///   <item>Si <see cref="EntraIdObjectId"/> es <c>null</c>, el handler
///         consulta <see cref="IEntraIdResolverPort.ResolverPorEmailAsync"/>
///         y, si retorna null (stub MVP), genera el OID pendiente
///         <c>pending:{email}</c>; el login lo sustituye por el real
///         (plan 15, F5).</item>
///   <item>409 <c>USUARIO_EMAIL_DUPLICADO</c> si <see cref="Email"/> ya
///         existe.</item>
///   <item>409 <c>USUARIO_OID_DUPLICADO</c> si <see cref="EntraIdObjectId"/>
///         no nulo y ya existe en BD.</item>
/// </list>
/// </summary>
public sealed record CrearUsuarioCommand(
    Guid Id,
    string Email,
    string? EntraIdObjectId,
    string NombreCompleto,
    Guid? DepartamentoId) : IRequest<UsuarioResponse>;

public sealed class CrearUsuarioValidator : AbstractValidator<CrearUsuarioCommand>
{
    // Regex email simple: algo@algo.algo. Suficiente para el endpoint
    // admin (la fuente de verdad de unicidad es la BD); aceptación
    // estricta vive en MS Graph cuando se haga la integración real.
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled);

    public CrearUsuarioValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .MaximumLength(254)
            .Must(BeValidEmail)
            .WithMessage("Email debe tener formato válido 'local@dominio.tld'.");

        RuleFor(c => c.NombreCompleto)
            .NotEmpty()
            .MaximumLength(254);

        RuleFor(c => c.EntraIdObjectId!)
            .MaximumLength(100)
            .When(c => c.EntraIdObjectId is not null);
    }

    private static bool BeValidEmail(string email)
        => !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);
}

public sealed class CrearUsuarioHandler : IRequestHandler<CrearUsuarioCommand, UsuarioResponse>
{
    private readonly IdentidadDbContext _db;
    private readonly IEntraIdResolverPort _entraResolver;

    public CrearUsuarioHandler(
        IdentidadDbContext db,
        IEntraIdResolverPort entraResolver)
    {
        _db = db;
        _entraResolver = entraResolver;
    }

    public async Task<UsuarioResponse> Handle(
        CrearUsuarioCommand command, CancellationToken cancellationToken)
    {
        // Validar email único antes de generar oid (cheaper, evita una
        // potencial llamada al resolver para inputs que igual fallarían).
        var emailDuplicado = await _db.Usuarios.AsNoTracking()
            .AnyAsync(u => u.Email == command.Email, cancellationToken);
        if (emailDuplicado)
        {
            throw new ConflictException(
                "USUARIO_EMAIL_DUPLICADO",
                $"Ya existe un usuario con email '{command.Email}'.");
        }

        // Resolución del EntraOid: explícito > resolver real > OID pendiente.
        string entraOid;
        string nombre = command.NombreCompleto;

        if (!string.IsNullOrWhiteSpace(command.EntraIdObjectId))
        {
            entraOid = command.EntraIdObjectId;
        }
        else
        {
            var resolved = await _entraResolver
                .ResolverPorEmailAsync(command.Email, cancellationToken);
            if (resolved is not null)
            {
                entraOid = resolved.ObjectId;
                // Si el caller no pasó override explícito en nombre, usa el
                // que devolvió Entra ID. Pero como NombreCompleto es required
                // en el command, respetamos lo que vino.
            }
            else
            {
                // OID pendiente (plan 15): "pending:" + email hasta que el
                // primer login lo vincule con el real. Sustituye al
                // placeholder histórico "dev-{email}", que sigue contando
                // como pendiente en Usuario.EsOidPendiente.
                entraOid = $"{Usuario.PrefijoOidPendiente}{command.Email}";
            }
        }

        // Validar OID único (siempre, incluso para placeholders dev — si
        // dos admins crean el mismo email a la vez, el primero gana).
        var oidDuplicado = await _db.Usuarios.AsNoTracking()
            .AnyAsync(u => u.EntraOid == entraOid, cancellationToken);
        if (oidDuplicado)
        {
            throw new ConflictException(
                "USUARIO_OID_DUPLICADO",
                $"Ya existe un usuario con EntraOid '{entraOid}'.");
        }

        var id = command.Id == Guid.Empty ? Guid.CreateVersion7() : command.Id;
        // Alta administrativa: la persona aún no inicia sesión (plan 15).
        var usuario = new Usuario(
            id, entraOid, command.Email, nombre,
            estadoAcceso: EstadoAcceso.PendientePrimerAcceso);
        if (command.DepartamentoId is Guid d)
        {
            usuario.AsignarDepartamento(d);
        }

        var preferencia = new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id);

        _db.Usuarios.Add(usuario);
        _db.UsuarioPreferencias.Add(preferencia);
        await _db.SaveChangesAsync(cancellationToken);

        return new UsuarioResponse(
            usuario.Id,
            usuario.Email,
            usuario.EntraOid,
            usuario.Nombre,
            usuario.DepartamentoId,
            usuario.Activo,
            usuario.Version);
    }
}
