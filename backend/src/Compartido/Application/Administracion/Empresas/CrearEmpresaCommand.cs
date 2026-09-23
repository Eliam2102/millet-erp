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
        // F1-ADM-01: Clave/domicilio/moneda son campos nuevos del dominio sin
        // captura todavía en este command (Fase 2 los expondrá en la API).
        // Clave = RFC (ya es business key única) evita colisión con
        // UNIQUE(clave); el domicilio queda con placeholders explícitos
        // hasta que Fase 2 agregue la captura real.
        var empresa = new Empresa(
            id,
            clave: command.Rfc.ToUpperInvariant(),
            command.Rfc,
            command.RazonSocial,
            command.RegimenFiscal,
            calle: "Sin especificar",
            numeroExterior: "S/N",
            colonia: "Sin especificar",
            ciudad: "Sin especificar",
            municipio: "Sin especificar",
            estado: "Sin especificar",
            pais: "México",
            nombreComercial: command.NombreComercial);

        _db.Empresas.Add(empresa);
        // Encolar antes de SaveChanges permite que el interceptor Outbox
        // persista el evento en la misma transacción que el agregado.
        await _events.PublishAsync(
            new EmpresaCreadaEvent(
                empresa.Id,
                empresa.Rfc,
                empresa.RazonSocial,
                _clock.UtcNow),
            cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

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
