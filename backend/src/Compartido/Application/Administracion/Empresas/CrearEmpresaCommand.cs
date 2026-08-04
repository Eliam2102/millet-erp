using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Events;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empresas;

/// <summary>
/// Crea una empresa nueva en <c>compartido.empresas</c> (F-Admin-PR2.3).
///
/// <list type="bullet">
///   <item><see cref="Id"/> = <see cref="Guid.Empty"/> ⇒ se genera con
///         <c>Guid.CreateVersion7()</c>. Permite que tests deterministas
///         puedan controlar el id explícitamente.</item>
///   <item>UNIQUE(rfc) → 409 <c>EMPRESA_RFC_DUPLICADO</c> si choca
///         (<see cref="ConflictException"/>).</item>
///   <item>Publica <see cref="EmpresaCreadaEvent"/> via
///         <see cref="IIntegrationEventPublisher"/>.</item>
/// </list>
/// </summary>
public sealed record CrearEmpresaCommand(
    Guid Id,
    string Rfc,
    string RazonSocial,
    string RegimenFiscal,
    string? NombreComercial) : IRequest<EmpresaResponse>;

public sealed class CrearEmpresaValidator : AbstractValidator<CrearEmpresaCommand>
{
    public CrearEmpresaValidator()
    {
        RuleFor(c => c.Rfc).NotEmpty().Length(12, 13);
        RuleFor(c => c.RazonSocial).NotEmpty().MaximumLength(254);
        RuleFor(c => c.RegimenFiscal).NotEmpty().MaximumLength(10);
        RuleFor(c => c.NombreComercial!).MaximumLength(254)
            .When(c => c.NombreComercial is not null);
    }
}

public sealed class CrearEmpresaHandler
    : IRequestHandler<CrearEmpresaCommand, EmpresaResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly IClock _clock;

    public CrearEmpresaHandler(
        CompartidoDbContext db,
        IIntegrationEventPublisher events,
        IClock clock)
    {
        _db = db;
        _events = events;
        _clock = clock;
    }

    public async Task<EmpresaResponse> Handle(
        CrearEmpresaCommand command, CancellationToken cancellationToken)
    {
        var existeRfc = await _db.Empresas.AsNoTracking()
            .AnyAsync(e => e.Rfc == command.Rfc, cancellationToken);
        if (existeRfc)
        {
            throw new ConflictException(
                "EMPRESA_RFC_DUPLICADO",
                $"Ya existe una empresa con RFC '{command.Rfc}'.");
        }

        var id = command.Id == Guid.Empty ? Guid.CreateVersion7() : command.Id;
        var empresa = new Empresa(
            id,
            command.Rfc,
            command.RazonSocial,
            command.RegimenFiscal,
            command.NombreComercial);

        _db.Empresas.Add(empresa);
        await _db.SaveChangesAsync(cancellationToken);

        // PLATFORM-TODO(<AdminOutbox>): el publisher solo encola al buffer
        // scoped; CompartidoDbContext no tiene OutboxSaveChangesInterceptor
        // wireado y el evento se pierde al cerrar el scope. Aceptable en
        // MVP — ningún consumer activo todavía.
        await _events.PublishAsync(
            new EmpresaCreadaEvent(
                empresa.Id,
                empresa.Rfc,
                empresa.RazonSocial,
                _clock.UtcNow),
            cancellationToken);

        return new EmpresaResponse(
            empresa.Id,
            empresa.Rfc,
            empresa.RazonSocial,
            empresa.NombreComercial,
            empresa.RegimenFiscal,
            empresa.TasaIvaDefault,
            empresa.CodigoPostal,
            empresa.Activa,
            empresa.Version);
    }
}
