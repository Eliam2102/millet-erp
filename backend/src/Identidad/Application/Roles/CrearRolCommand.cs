using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Roles;

/// <summary>
/// Crea un rol nuevo en <c>identidad.roles</c> (F-Admin-PR3.2).
///
/// <list type="bullet">
///   <item><see cref="Id"/> = <see cref="Guid.Empty"/> ⇒ se genera con
///         <c>Guid.CreateVersion7()</c>. Tests deterministas pueden pasar
///         un id concreto.</item>
///   <item><see cref="Codigo"/> validado como kebab-lowercase comenzando
///         con letra (regex <c>^[a-z][a-z0-9-]*$</c>). Es el natural-key
///         del rol y no se edita después de crear. Mismo formato que los
///         roles del bootstrap (<c>super-admin</c>, <c>admin-compras</c>,
///         <c>auditor</c>, etc.). UNIQUE(codigo) → 409
///         <c>ROL_CODIGO_DUPLICADO</c>.</item>
///   <item>El rol se crea con <c>EsDelSistema=false</c> y
///         <c>Activo=true</c> — los roles del sistema solo los crea el
///         bootstrap del super-admin.</item>
/// </list>
/// </summary>
public sealed record CrearRolCommand(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Descripcion) : IRequest<RolResponse>;

public sealed class CrearRolValidator : AbstractValidator<CrearRolCommand>
{
    // Espejo del schema Zod del frontend (CrearRolSchema).
    private static readonly Regex CodigoRegex = new(
        "^[a-z][a-z0-9-]*$",
        RegexOptions.Compiled);

    public CrearRolValidator()
    {
        RuleFor(c => c.Codigo)
            .NotEmpty()
            .MaximumLength(64)
            .Must(c => c is not null && CodigoRegex.IsMatch(c))
            .WithMessage("Codigo debe contener solo letras minúsculas, " +
                         "dígitos y guiones, y comenzar con letra.");
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Descripcion!).MaximumLength(500)
            .When(c => c.Descripcion is not null);
    }
}

public sealed class CrearRolHandler : IRequestHandler<CrearRolCommand, RolResponse>
{
    private readonly IdentidadDbContext _db;

    public CrearRolHandler(IdentidadDbContext db) => _db = db;

    public async Task<RolResponse> Handle(
        CrearRolCommand command, CancellationToken cancellationToken)
    {
        var existe = await _db.Roles.AsNoTracking()
            .AnyAsync(r => r.Codigo == command.Codigo, cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "ROL_CODIGO_DUPLICADO",
                $"Ya existe un rol con código '{command.Codigo}'.");
        }

        var id = command.Id == Guid.Empty ? Guid.CreateVersion7() : command.Id;
        var rol = new Rol(id, command.Codigo, command.Nombre,
            esDelSistema: false, descripcion: command.Descripcion);

        _db.Roles.Add(rol);
        await _db.SaveChangesAsync(cancellationToken);

        return new RolResponse(
            rol.Id, rol.Codigo, rol.Nombre, rol.Descripcion,
            rol.EsDelSistema, rol.Activo, rol.Version);
    }
}
