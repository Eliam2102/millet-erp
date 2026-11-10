using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Application.NotasCredito.SolicitarReembolso;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.NotasCredito.AutorizarNotaCredito;

/// <summary>
/// Autoriza y timbra una nota de crédito de A+W que está «Por autorizar» (86e3wjt5r).
/// M-087: el gerente (<c>facturacion.notas-credito.autorizar</c>) la autoriza hasta el umbral
/// (<see cref="NotasCreditoAwOptions.UmbralSegundaFirmaMxn"/>, 35,000 MXN); arriba de él firma también
/// Crédito y Cobranza (<c>facturacion.notas-credito.autorizar-credito</c>, otra persona). Se timbra al
/// completar las firmas que pide, en cualquier orden.
/// </summary>
public sealed record AutorizarNotaCreditoCommand(Guid NotaCreditoId, ReembolsoClienteRequest? Reembolso = null)
    : IRequest<AutorizarNotaCreditoResponse>;

/// <summary>F6.3: la cuenta destino del reembolso, si viene, con la misma forma que en la bonificación.</summary>
public sealed class AutorizarNotaCreditoValidator : AbstractValidator<AutorizarNotaCreditoCommand>
{
    public AutorizarNotaCreditoValidator()
    {
        RuleFor(c => c.NotaCreditoId).NotEmpty();
        RuleFor(c => c.Reembolso!).SetValidator(new ReembolsoClienteRequestValidator()).When(c => c.Reembolso is not null);
    }
}

public sealed record AutorizarNotaCreditoResponse(Guid Id, string Estado, string? Uuid, string Folio, decimal Total,
    bool RequiereFirmaCredito = false, bool FirmadaGerente = false, bool FirmadaCredito = false, string? Pendiente = null);

/// <summary>M-087: firma de Crédito y Cobranza en una nota de A+W arriba del umbral.</summary>
public sealed record FirmarCreditoNotaCreditoCommand(Guid NotaCreditoId) : IRequest<AutorizarNotaCreditoResponse>;

/// <summary>M-087: umbral de la segunda firma (Crédito y Cobranza) de las notas de crédito de A+W.</summary>
public sealed class NotasCreditoAwOptions
{
    public const string SectionName = "Facturacion:NotasCreditoAw";

    /// <summary>Importe en MXN (con IVA) hasta el que basta la autorización del gerente.</summary>
    public decimal UmbralSegundaFirmaMxn { get; set; } = 35_000m;

    /// <summary>Importe de la nota en MXN (con su tipo de cambio si viene en otra moneda).</summary>
    public static decimal ImporteMxn(Domain.NotasCredito.NotaCredito nc) =>
        Math.Round(nc.Total * (nc.TipoCambio ?? 1m), 2, MidpointRounding.AwayFromZero);

    public bool RequiereFirmaCredito(Domain.NotasCredito.NotaCredito nc) => ImporteMxn(nc) > UmbralSegundaFirmaMxn;
}

public sealed class AutorizarNotaCreditoHandler(
    FacturacionDbContext db,
    IPeriodoContablePort periodo,
    ISaldoFacturaReadPort saldos,
    ICfdiTimbradoPort fiscal,
    ICfdiRepositorioPort cfdiRepo,
    IIntegrationEventPublisher eventos,
    ICurrentUserContext user,
    IClock clock,
    ICurrentUserPermissions permisos,
    Microsoft.Extensions.Options.IOptions<NotasCreditoAwOptions>? opciones = null)
    : IRequestHandler<AutorizarNotaCreditoCommand, AutorizarNotaCreditoResponse>,
      IRequestHandler<FirmarCreditoNotaCreditoCommand, AutorizarNotaCreditoResponse>
{
    private readonly NotasCreditoAwOptions _umbral = opciones?.Value ?? new NotasCreditoAwOptions();

    /// <summary>M-087: Crédito y Cobranza firma; si el gerente ya firmó, se timbra.</summary>
    public async Task<AutorizarNotaCreditoResponse> Handle(FirmarCreditoNotaCreditoCommand command, CancellationToken cancellationToken)
    {
        var nc = await CargarAsync(command.NotaCreditoId, cancellationToken);
        if (!_umbral.RequiereFirmaCredito(nc))
            throw new BusinessRuleException("NC_NO_REQUIERE_FIRMA_CREDITO",
                $"La nota de crédito es de {NotasCreditoAwOptions.ImporteMxn(nc):N2} MXN; hasta {_umbral.UmbralSegundaFirmaMxn:N2} la autoriza solo el gerente.");
        var ahora = clock.UtcNow;
        nc.RegistrarFirmaCredito(user.UserId, ahora);
        if (nc.FirmaGerentePorId is null)
        {
            await db.SaveChangesAsync(cancellationToken);
            return Respuesta(nc, "Gerente");
        }
        return await TimbrarAsync(nc, ahora, cancellationToken);
    }

    public async Task<AutorizarNotaCreditoResponse> Handle(AutorizarNotaCreditoCommand command, CancellationToken cancellationToken)
    {
        if (command.Reembolso is not null)
            await ReembolsoNotaCredito.ExigirPermisoAsync(permisos, cancellationToken);

        var nc = await CargarAsync(command.NotaCreditoId, cancellationToken);
        var ahora = clock.UtcNow;

        if (command.Reembolso is { } reembolso)
        {
            var facturaReembolso = await db.FacturasVenta.FirstOrDefaultAsync(f => f.Id == nc.FacturaRelacionadaId, cancellationToken)
                ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", "No existe la factura de origen de la nota de crédito.");
            await ReembolsoNotaCredito.ExigirClienteAsync(db, facturaReembolso, cancellationToken);
            nc.ProgramarReembolso(reembolso.Banco, reembolso.Clabe, reembolso.Titular);
        }

        // M-087: arriba del umbral, la firma del gerente sola no timbra; espera a Crédito y Cobranza.
        if (_umbral.RequiereFirmaCredito(nc))
        {
            nc.RegistrarFirmaGerente(user.UserId, ahora);
            if (nc.FirmaCreditoPorId is null)
            {
                await db.SaveChangesAsync(cancellationToken);
                return Respuesta(nc, "CreditoYCobranza");
            }
        }
        return await TimbrarAsync(nc, ahora, cancellationToken);
    }

    private async Task<Domain.NotasCredito.NotaCredito> CargarAsync(Guid id, CancellationToken cancellationToken)
    {
        var nc = await db.NotasCredito.Include(n => n.Relaciones).FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("NOTA_CREDITO_NO_ENCONTRADA", $"No existe la nota de crédito {id}.");
        if (nc.Estado != EstadoTimbrado.PorAutorizar)
            throw new BusinessRuleException("NC_NO_POR_AUTORIZAR",
                $"Solo una nota de crédito Por autorizar puede autorizarse (estado actual: {nc.Estado}).");
        return nc;
    }

    private AutorizarNotaCreditoResponse Respuesta(Domain.NotasCredito.NotaCredito nc, string? pendiente) =>
        new(nc.Id, nc.Estado.ToString(), nc.Uuid, nc.Folio, nc.Total, _umbral.RequiereFirmaCredito(nc),
            nc.FirmaGerentePorId is not null, nc.FirmaCreditoPorId is not null, pendiente);

    /// <summary>Firmas completas: valida periodo y saldo, autoriza y timbra (flujo de 86e3wjt5r).</summary>
    private async Task<AutorizarNotaCreditoResponse> TimbrarAsync(
        Domain.NotasCredito.NotaCredito nc, DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        var fechaFiscal = Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.FechaFiscal(ahora); // período en la fecha fiscal de México, no en UTC
        if (!await periodo.EstaAbiertoAsync(fechaFiscal.Year, fechaFiscal.Month, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO", $"El período contable {fechaFiscal.Year}-{fechaFiscal.Month:D2} está cerrado; no se puede emitir.");

        // El saldo pudo bajar desde que llegó la nota (pagos u otras notas timbradas).
        var factura = await db.FacturasVenta.FirstOrDefaultAsync(f => f.Id == nc.FacturaRelacionadaId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", "No existe la factura de origen de la nota de crédito.");
        if (factura.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException("FACTURA_NO_TIMBRADA",
                $"La factura de origen ya no está timbrada vigente (estado actual: {factura.Estado}).");
        var saldo = await saldos.ObtenerSaldoAsync(factura.Id, cancellationToken) ?? factura.Total;
        if (nc.Total > saldo)
            throw new BusinessRuleException("NC_IMPORTE_MAYOR_SALDO",
                $"El importe de la nota ({nc.Total:N2}) es mayor que el saldo de la factura ({saldo:N2}).");

        nc.Autorizar(user.UserId, ahora);
        await TimbradoEjecutor.TimbrarYAplicarAsync(db, nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
            fiscal, cfdiRepo, ahora, cancellationToken);

        if (nc.Estado == EstadoTimbrado.Timbrado)
            await eventos.PublishAsync(EventosContablesFacturacion.NotaCreditoTimbrada(
                nc, ahora, factura.Id, clienteId: await ClienteContableFacturacion.ResolverAsync(db, factura, cancellationToken)), cancellationToken);

        // F6.3: con «Devolver dinero al cliente», la solicitud a Tesorería sale en la misma transacción que el timbre.
        await ReembolsoNotaCredito.SolicitarProgramadoSiAplicaAsync(db, eventos, nc, user.UserId, ahora, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Respuesta(nc, null);
    }
}
