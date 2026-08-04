using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// Alta manual de cliente en <c>compartido.clientes</c> (ADR-0048 D6).
/// UNIQUE(clave) y UNIQUE(referencia_externa) → 422 legible si chocan.
/// La auto-provisión desde A+W NO usa este command (usa
/// <c>ProvisionarClienteDesdeAwCommand</c>, PR4) — este es el alta de
/// operador con <see cref="OrigenMaster.Manual"/>.
/// </summary>
public sealed record CrearClienteCommand(
    string Clave,
    string RazonSocial,
    string? ReferenciaExterna,
    string? Rfc,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? UsoCfdiDefault,
    string? FormaPagoDefault,
    string? MetodoPagoDefault,
    string MonedaDefault,
    bool EsGenerico,
    string? Email,
    string? Telefono,
    string? NumRegIdTrib = null,
    string? PaisResidencia = null,
    string? DomicilioExtranjeroCalle = null,
    string? DomicilioExtranjeroEstado = null,
    string? DomicilioExtranjeroCodigoPostal = null) : IRequest<CrearClienteResponse>;

public sealed record CrearClienteResponse(Guid Id, string Clave);

public sealed class CrearClienteValidator : AbstractValidator<CrearClienteCommand>
{
    public CrearClienteValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.RazonSocial).NotEmpty().MaximumLength(254);
        RuleFor(c => c.ReferenciaExterna!).MaximumLength(50)
            .When(c => c.ReferenciaExterna is not null);
        RuleFor(c => c.Rfc!).Length(12, 13).When(c => c.Rfc is not null);
        RuleFor(c => c.RegimenFiscal!).Length(3).When(c => c.RegimenFiscal is not null);
        RuleFor(c => c.CodigoPostalFiscal!).Length(5).When(c => c.CodigoPostalFiscal is not null);
        RuleFor(c => c.UsoCfdiDefault!).MaximumLength(4).When(c => c.UsoCfdiDefault is not null);
        RuleFor(c => c.FormaPagoDefault!).Length(2).When(c => c.FormaPagoDefault is not null);
        RuleFor(c => c.MetodoPagoDefault!).Must(m => m is "PUE" or "PPD")
            .WithMessage("El método de pago debe ser PUE o PPD.")
            .When(c => c.MetodoPagoDefault is not null);
        RuleFor(c => c.MonedaDefault).NotEmpty().Length(3);
        RuleFor(c => c.Email!).MaximumLength(254).EmailAddress()
            .When(c => !string.IsNullOrEmpty(c.Email));
        RuleFor(c => c.Telefono!).MaximumLength(50).When(c => c.Telefono is not null);
        RuleFor(c => c.NumRegIdTrib!).MaximumLength(40).When(c => c.NumRegIdTrib is not null);
        RuleFor(c => c.PaisResidencia!).Matches("^[A-Za-z]{3}$")
            .WithMessage("El país de residencia debe ser clave SAT c_Pais (ISO alfa-3, 3 letras).")
            .When(c => c.PaisResidencia is not null);
        RuleFor(c => c.DomicilioExtranjeroCalle!).MaximumLength(200).When(c => c.DomicilioExtranjeroCalle is not null);
        RuleFor(c => c.DomicilioExtranjeroEstado!).MaximumLength(100).When(c => c.DomicilioExtranjeroEstado is not null);
        RuleFor(c => c.DomicilioExtranjeroCodigoPostal!).MaximumLength(12).When(c => c.DomicilioExtranjeroCodigoPostal is not null);
    }
}

public sealed class CrearClienteHandler
    : IRequestHandler<CrearClienteCommand, CrearClienteResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearClienteHandler(CompartidoDbContext db) => _db = db;

    public async Task<CrearClienteResponse> Handle(
        CrearClienteCommand request, CancellationToken cancellationToken)
    {
        var claveExiste = await _db.Clientes.AsNoTracking()
            .AnyAsync(c => c.Clave == request.Clave, cancellationToken);
        if (claveExiste)
        {
            throw new BusinessRuleException(
                "CLIENTE_CLAVE_DUPLICADA",
                $"Ya existe un cliente con clave '{request.Clave}'.");
        }

        if (request.ReferenciaExterna is not null)
        {
            var refExiste = await _db.Clientes.AsNoTracking()
                .AnyAsync(c => c.ReferenciaExterna == request.ReferenciaExterna, cancellationToken);
            if (refExiste)
            {
                throw new BusinessRuleException(
                    "CLIENTE_REFERENCIA_DUPLICADA",
                    $"Ya existe un cliente con referencia externa '{request.ReferenciaExterna}'.");
            }
        }

        var cliente = new Cliente(
            id: Guid.CreateVersion7(),
            clave: request.Clave,
            razonSocial: request.RazonSocial,
            origen: OrigenMaster.Manual,
            referenciaExterna: request.ReferenciaExterna,
            rfc: request.Rfc,
            regimenFiscal: request.RegimenFiscal,
            codigoPostalFiscal: request.CodigoPostalFiscal,
            usoCfdiDefault: request.UsoCfdiDefault,
            formaPagoDefault: request.FormaPagoDefault,
            metodoPagoDefault: request.MetodoPagoDefault,
            monedaDefault: request.MonedaDefault,
            esGenerico: request.EsGenerico,
            email: request.Email,
            telefono: request.Telefono,
            numRegIdTrib: request.NumRegIdTrib,
            paisResidencia: request.PaisResidencia,
            domicilioExtranjeroCalle: request.DomicilioExtranjeroCalle,
            domicilioExtranjeroEstado: request.DomicilioExtranjeroEstado,
            domicilioExtranjeroCodigoPostal: request.DomicilioExtranjeroCodigoPostal);

        _db.Clientes.Add(cliente);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearClienteResponse(cliente.Id, cliente.Clave);
    }
}
