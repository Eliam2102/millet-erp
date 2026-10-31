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
/// Autoriza y timbra, en un solo paso, una nota de crédito de A+W que está «Por autorizar» (86e3wjt5r).
/// Propuesta mientras no exista la cadena por monto (F6.4): la autoriza quien tenga el permiso
/// <c>facturacion.notas-credito.autorizar</c>; cuando llegue F6.4 la reemplaza.
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

public sealed record AutorizarNotaCreditoResponse(Guid Id, string Estado, string? Uuid, string Folio, decimal Total);

public sealed class AutorizarNotaCreditoHandler(
    FacturacionDbContext db,
    IPeriodoContablePort periodo,
    ISaldoFacturaReadPort saldos,
    ICfdiTimbradoPort fiscal,
    ICfdiRepositorioPort cfdiRepo,
    IIntegrationEventPublisher eventos,
    ICurrentUserContext user,
    IClock clock,
    ICurrentUserPermissions permisos)
    : IRequestHandler<AutorizarNotaCreditoCommand, AutorizarNotaCreditoResponse>
{
    public async Task<AutorizarNotaCreditoResponse> Handle(AutorizarNotaCreditoCommand command, CancellationToken cancellationToken)
    {
        if (command.Reembolso is not null)
            await ReembolsoNotaCredito.ExigirPermisoAsync(permisos, cancellationToken);

        var nc = await db.NotasCredito.Include(n => n.Relaciones).FirstOrDefaultAsync(n => n.Id == command.NotaCreditoId, cancellationToken)
            ?? throw new EntityNotFoundException("NOTA_CREDITO_NO_ENCONTRADA", $"No existe la nota de crédito {command.NotaCreditoId}.");
        if (nc.Estado != EstadoTimbrado.PorAutorizar)
            throw new BusinessRuleException("NC_NO_POR_AUTORIZAR",
                $"Solo una nota de crédito Por autorizar puede autorizarse (estado actual: {nc.Estado}).");

        var ahora = clock.UtcNow;
        if (!await periodo.EstaAbiertoAsync(ahora.Year, ahora.Month, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO", $"El período contable {ahora.Year}-{ahora.Month:D2} está cerrado; no se puede emitir.");

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

        if (command.Reembolso is { } reembolso)
        {
            await ReembolsoNotaCredito.ExigirClienteAsync(db, factura, cancellationToken);
            nc.ProgramarReembolso(reembolso.Banco, reembolso.Clabe, reembolso.Titular);
        }
        nc.Autorizar(user.UserId, ahora);
        await TimbradoEjecutor.TimbrarYAplicarAsync(db, nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
            fiscal, cfdiRepo, ahora, cancellationToken);

        if (nc.Estado == EstadoTimbrado.Timbrado)
            await eventos.PublishAsync(EventosContablesFacturacion.NotaCreditoTimbrada(
                nc, ahora, factura.Id, clienteId: await ClienteContableFacturacion.ResolverAsync(db, factura, cancellationToken)), cancellationToken);

        // F6.3: con «Devolver dinero al cliente», la solicitud a Tesorería sale en la misma transacción que el timbre.
        await ReembolsoNotaCredito.SolicitarProgramadoSiAplicaAsync(db, eventos, nc, user.UserId, ahora, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return new AutorizarNotaCreditoResponse(nc.Id, nc.Estado.ToString(), nc.Uuid, nc.Folio, nc.Total);
    }
}
