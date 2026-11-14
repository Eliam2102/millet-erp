using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Anticipos.RegistrarAnticipoExterno;

/// <summary>
/// P45: registra un anticipo cuyo CFDI se timbró fuera del ERP (SAP/A+W) para que su saldo vivo aparezca en
/// «Anticipos a aplicar» y la NC 07 relacione su UUID. No timbra, no reserva folio FANT y no publica el asiento del
/// anticipo (ya está en la contabilidad de origen).
/// </summary>
public sealed record RegistrarAnticipoExternoCommand(
    Guid ClienteId,
    string ReceptorRfc,
    Guid SucursalId,
    string Uuid,
    string Folio,
    DateTimeOffset FechaTimbrado,
    string Moneda,
    decimal? TipoCambio,
    string FormaPago,
    decimal Total,
    decimal? TasaIvaTraslado,
    decimal SaldoVivo,
    string? PedidoOrigenRef) : IRequest<RegistrarAnticipoExternoResponse>;

public sealed record RegistrarAnticipoExternoResponse(Guid AnticipoId, Guid FacturaAnticipoId, string Folio, string Uuid, decimal Total, decimal Saldo);

public sealed class RegistrarAnticipoExternoValidator : AbstractValidator<RegistrarAnticipoExternoCommand>
{
    public RegistrarAnticipoExternoValidator()
    {
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.ReceptorRfc).NotEmpty().MaximumLength(13);
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.Uuid).NotEmpty().Must(u => Guid.TryParse(u, out _)).WithMessage("El UUID del CFDI no es válido.");
        RuleFor(c => c.Folio).NotEmpty().MaximumLength(30);
        RuleFor(c => c.Moneda).NotEmpty().Must(m => m is "MXN" or "USD").WithMessage("La moneda del anticipo es MXN o USD.");
        RuleFor(c => c.TipoCambio).NotNull().GreaterThan(0).When(c => c.Moneda == "USD");
        RuleFor(c => c.FormaPago).NotEmpty().MaximumLength(2);
        RuleFor(c => c.Total).GreaterThan(0);
        RuleFor(c => c.SaldoVivo).GreaterThan(0).LessThanOrEqualTo(c => c.Total)
            .WithMessage("El saldo vivo debe ser mayor que cero y no mayor que el total.");
        RuleFor(c => c.TasaIvaTraslado).InclusiveBetween(0m, 0.16m).When(c => c.TasaIvaTraslado is not null);
        RuleFor(c => c.PedidoOrigenRef).MaximumLength(50);
    }
}

public sealed class RegistrarAnticipoExternoHandler(
    FacturacionDbContext db,
    IClientesReadPort clientes,
    IEmpresaFiscalReadPort empresasFiscal,
    ICurrentEmpresaContext empresa,
    ICurrentUserContext user)
    : IRequestHandler<RegistrarAnticipoExternoCommand, RegistrarAnticipoExternoResponse>
{
    /// <summary>Prefijo del folio interno: no choca con la serie FANT del ERP en la misma sucursal.</summary>
    public const string PrefijoFolio = "EXT-";

    public async Task<RegistrarAnticipoExternoResponse> Handle(RegistrarAnticipoExternoCommand request, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var uuid = request.Uuid.Trim().ToUpperInvariant();
        var uuidMinusculas = uuid.ToLowerInvariant();
        if (await db.Comprobantes.AnyAsync(c => c.Uuid == uuid || c.Uuid == uuidMinusculas, cancellationToken))
            throw new BusinessRuleException("ANTICIPO_EXTERNO_UUID_DUPLICADO", "Ese UUID ya está registrado en el ERP.");

        var cliente = await clientes.ObtenerAsync(request.ClienteId, cancellationToken)
            ?? throw new EntityNotFoundException("CLIENTE_NO_ENCONTRADO", "El cliente no existe en el catálogo.");
        if (string.IsNullOrWhiteSpace(cliente.Rfc) || cliente.EsGenerico)
            throw new BusinessRuleException("ANTICIPO_EXTERNO_CLIENTE_SIN_RFC",
                "El cliente no tiene RFC nominal en el catálogo; corrígelo antes de registrar su anticipo.");
        if (!string.Equals(cliente.Rfc.Trim(), request.ReceptorRfc.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("ANTICIPO_EXTERNO_RFC_DISTINTO",
                "El RFC receptor del CFDI no coincide con el RFC del cliente en el catálogo.");

        var folio = $"{PrefijoFolio}{request.Folio.Trim()}";
        if (await db.Comprobantes.AnyAsync(c => c.SucursalId == request.SucursalId && c.Folio == folio, cancellationToken))
            throw new BusinessRuleException("ANTICIPO_EXTERNO_FOLIO_DUPLICADO", $"El folio {request.Folio.Trim()} ya está registrado en la sucursal.");

        var receptor = new DatosFiscalesReceptor(
            Rfc: cliente.Rfc,
            Nombre: cliente.RazonSocial,
            RegimenFiscal: cliente.RegimenFiscal ?? string.Empty,
            CodigoPostal: cliente.CodigoPostalFiscal ?? string.Empty,
            UsoCfdi: "G03",
            Pais: "MEX",
            EsGenerico: false);
        var emisor = await EmisorSnapshot.ResolverAsync(empresasFiscal, empresaId, cancellationToken);

        var anticipoId = Guid.CreateVersion7();
        var tipo = request.Moneda == "USD" ? TipoAnticipo.ClientesUsd : TipoAnticipo.ClientesMxp;
        var factura = FacturaAnticipo.RegistrarExterna(
            empresaId, folio, request.SucursalId, user.UserId, receptor, emisor, request.FormaPago, request.Moneda,
            request.Moneda == "USD" ? request.TipoCambio : null, request.FechaTimbrado, tipo, pedidoFacturableId: null,
            anticipoId, uuid, request.Total, request.TasaIvaTraslado);
        var anticipo = Anticipo.CrearExterno(
            empresaId, request.ClienteId, cliente.Rfc, tipo, request.Moneda, factura.Total, request.SaldoVivo,
            factura.Id, request.PedidoOrigenRef, anticipoId);

        db.Comprobantes.Add(factura);
        db.Anticipos.Add(anticipo);
        await db.SaveChangesAsync(cancellationToken);

        return new RegistrarAnticipoExternoResponse(anticipo.Id, factura.Id, factura.Folio, uuid, factura.Total, anticipo.Saldo);
    }
}
