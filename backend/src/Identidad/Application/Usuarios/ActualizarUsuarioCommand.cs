using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// PATCH parcial sobre un usuario existente (F-Admin-PR4.2). Campos
/// <c>null</c> ⇒ no tocar; para limpiar <see cref="DepartamentoId"/>
/// se pasa <see cref="LimpiarDepartamento"/> = <c>true</c>.
///
/// <list type="bullet">
///   <item>404 <c>USUARIO_NO_ENCONTRADO</c> si no existe.</item>
///   <item>409 <c>USUARIO_EMAIL_DUPLICADO</c> si nuevo email choca con
///         otro usuario.</item>
/// </list>
/// </summary>
public sealed record ActualizarUsuarioCommand(
    Guid Id,
    string? Email,
    string? NombreCompleto,
    Guid? DepartamentoId,
    bool LimpiarDepartamento) : IRequest<UsuarioResponse>;

public sealed class ActualizarUsuarioValidator : AbstractValidator<ActualizarUsuarioCommand>
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled);

    public ActualizarUsuarioValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Email!)
            .NotEmpty()
            .MaximumLength(254)
            .Must(BeValidEmail)
            .WithMessage("Email debe tener formato válido 'local@dominio.tld'.")
            .When(c => c.Email is not null);
        RuleFor(c => c.NombreCompleto!)
            .NotEmpty()
            .MaximumLength(254)
            .When(c => c.NombreCompleto is not null);
    }

    private static bool BeValidEmail(string email)
        => !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);
}

public sealed class ActualizarUsuarioHandler
    : IRequestHandler<ActualizarUsuarioCommand, UsuarioResponse>
{
    private readonly IdentidadDbContext _db;

    public ActualizarUsuarioHandler(IdentidadDbContext db) => _db = db;

    public async Task<UsuarioResponse> Handle(
        ActualizarUsuarioCommand command, CancellationToken cancellationToken)
    {
        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_NO_ENCONTRADO",
                $"No existe usuario con id '{command.Id}'.");

        // Validar email único si está cambiando.
        if (command.Email is not null && command.Email != usuario.Email)
        {
            var duplicado = await _db.Usuarios.AsNoTracking()
                .AnyAsync(
                    u => u.Email == command.Email && u.Id != command.Id,
                    cancellationToken);
            if (duplicado)
            {
                throw new ConflictException(
                    "USUARIO_EMAIL_DUPLICADO",
                    $"Ya existe otro usuario con email '{command.Email}'.");
            }
        }

        usuario.ActualizarPerfil(
            command.Email,
            command.NombreCompleto,
            command.DepartamentoId,
            command.LimpiarDepartamento);

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
