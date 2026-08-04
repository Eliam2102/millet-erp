using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// PATCH parcial de cliente (mismo contrato que
/// <c>ActualizarProveedorCommand</c>): <c>null</c> = no tocar; flag
/// <c>LimpiarX</c> = setear nullable a null. Inmutables: `Id`, `Clave`,
/// `ReferenciaExterna` (correlación con A+W) y `Origen`. Es la vía para
/// completar RFC/régimen/CP de clientes auto-provisionados antes de timbrar.
/// </summary>
public sealed record ActualizarClienteCommand(
    Guid ClienteId,
    string? RazonSocial = null,
    string? Rfc = null,
    string? RegimenFiscal = null,
    string? CodigoPostalFiscal = null,
    string? UsoCfdiDefault = null,
    string? FormaPagoDefault = null,
    string? MetodoPagoDefault = null,
    string? MonedaDefault = null,
    bool? EsGenerico = null,
    string? Email = null,
    string? Telefono = null,
    string? NumRegIdTrib = null,
    string? PaisResidencia = null,
    string? DomicilioExtranjeroCalle = null,
    string? DomicilioExtranjeroEstado = null,
    string? DomicilioExtranjeroCodigoPostal = null,
    bool LimpiarRfc = false,
    bool LimpiarRegimenFiscal = false,
    bool LimpiarCodigoPostalFiscal = false,
    bool LimpiarUsoCfdiDefault = false,
    bool LimpiarFormaPagoDefault = false,
    bool LimpiarMetodoPagoDefault = false,
    bool LimpiarEmail = false,
    bool LimpiarTelefono = false,
    bool LimpiarNumRegIdTrib = false,
    bool LimpiarPaisResidencia = false,
    bool LimpiarDomicilioExtranjeroCalle = false,
    bool LimpiarDomicilioExtranjeroEstado = false,
    bool LimpiarDomicilioExtranjeroCodigoPostal = false) : IRequest;

public sealed class ActualizarClienteValidator : AbstractValidator<ActualizarClienteCommand>
{
    public ActualizarClienteValidator()
    {
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.RazonSocial!).NotEmpty().MaximumLength(254)
            .When(c => c.RazonSocial is not null);
        RuleFor(c => c.Rfc!).Length(12, 13).When(c => c.Rfc is not null);
        RuleFor(c => c.RegimenFiscal!).Length(3).When(c => c.RegimenFiscal is not null);
        RuleFor(c => c.CodigoPostalFiscal!).Length(5).When(c => c.CodigoPostalFiscal is not null);
        RuleFor(c => c.UsoCfdiDefault!).MaximumLength(4).When(c => c.UsoCfdiDefault is not null);
        RuleFor(c => c.FormaPagoDefault!).Length(2).When(c => c.FormaPagoDefault is not null);
        RuleFor(c => c.MetodoPagoDefault!).Must(m => m is "PUE" or "PPD")
            .WithMessage("El método de pago debe ser PUE o PPD.")
            .When(c => c.MetodoPagoDefault is not null);
        RuleFor(c => c.MonedaDefault!).Length(3).When(c => c.MonedaDefault is not null);
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

public sealed class ActualizarClienteHandler : IRequestHandler<ActualizarClienteCommand>
{
    private readonly CompartidoDbContext _db;

    public ActualizarClienteHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(ActualizarClienteCommand request, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes
            .FirstOrDefaultAsync(c => c.Id == request.ClienteId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CLIENTE_NO_ENCONTRADO",
                $"No existe cliente con id '{request.ClienteId}'.");

        cliente.ActualizarDatos(
            razonSocial: request.RazonSocial,
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
            limpiarRfc: request.LimpiarRfc,
            limpiarRegimenFiscal: request.LimpiarRegimenFiscal,
            limpiarCodigoPostalFiscal: request.LimpiarCodigoPostalFiscal,
            limpiarUsoCfdiDefault: request.LimpiarUsoCfdiDefault,
            limpiarFormaPagoDefault: request.LimpiarFormaPagoDefault,
            limpiarMetodoPagoDefault: request.LimpiarMetodoPagoDefault,
            limpiarEmail: request.LimpiarEmail,
            limpiarTelefono: request.LimpiarTelefono);

        cliente.AsignarDatosReceptorExtranjero(
            numRegIdTrib: request.NumRegIdTrib,
            paisResidencia: request.PaisResidencia,
            domicilioExtranjeroCalle: request.DomicilioExtranjeroCalle,
            domicilioExtranjeroEstado: request.DomicilioExtranjeroEstado,
            domicilioExtranjeroCodigoPostal: request.DomicilioExtranjeroCodigoPostal,
            limpiarNumRegIdTrib: request.LimpiarNumRegIdTrib,
            limpiarPaisResidencia: request.LimpiarPaisResidencia,
            limpiarDomicilioExtranjeroCalle: request.LimpiarDomicilioExtranjeroCalle,
            limpiarDomicilioExtranjeroEstado: request.LimpiarDomicilioExtranjeroEstado,
            limpiarDomicilioExtranjeroCodigoPostal: request.LimpiarDomicilioExtranjeroCodigoPostal);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
