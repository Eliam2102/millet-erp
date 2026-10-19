using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.NotasCredito.SolicitarReembolso;

/// <summary>
/// T9.6 · CA6.17: pide a Tesorería el reembolso en dinero de una NC timbrada. Publica
/// <c>facturacion.nota-credito.reembolso-solicitado.v1</c> por Outbox (ADR-0009) en la
/// misma transacción que marca la NC. No cambia el saldo por cobrar ([Decisión 13-K]).
/// </summary>
public sealed record SolicitarReembolsoNotaCreditoCommand(Guid NotaCreditoId, decimal Importe, string Motivo)
    : IRequest<SolicitarReembolsoNotaCreditoResponse>;

public sealed record SolicitarReembolsoNotaCreditoResponse(
    Guid NotaCreditoId, decimal Importe, DateTimeOffset ReembolsoSolicitadoEn);

public sealed class SolicitarReembolsoNotaCreditoValidator : AbstractValidator<SolicitarReembolsoNotaCreditoCommand>
{
    public SolicitarReembolsoNotaCreditoValidator()
    {
        RuleFor(c => c.NotaCreditoId).NotEmpty();
        RuleFor(c => c.Importe).GreaterThan(0);
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class SolicitarReembolsoNotaCreditoHandler
    : IRequestHandler<SolicitarReembolsoNotaCreditoCommand, SolicitarReembolsoNotaCreditoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly IAlcanceCajaEvaluator _alcance;

    public SolicitarReembolsoNotaCreditoHandler(
        FacturacionDbContext db, IIntegrationEventPublisher eventos, ICurrentUserContext user,
        IClock clock, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _eventos = eventos;
        _user = user;
        _clock = clock;
        _alcance = alcance;
    }

    public async Task<SolicitarReembolsoNotaCreditoResponse> Handle(
        SolicitarReembolsoNotaCreditoCommand command, CancellationToken cancellationToken)
    {
        var usuarioId = _user.UserId
            ?? throw new ForbiddenException("USUARIO_NO_AUTENTICADO", "No hay usuario autenticado en el contexto del request.");

        // Fuera de alcance → mismo 404 que inexistente (12-cajas.md §4.1).
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var nc = await alcance.AplicarA(_db.NotasCredito)
            .FirstOrDefaultAsync(x => x.Id == command.NotaCreditoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "NOTA_CREDITO_NO_ENCONTRADA", $"No existe la nota de crédito '{command.NotaCreditoId}'.");

        var clienteId = await ClienteContableFacturacion.ResolverAsync(_db, nc, cancellationToken)
            ?? throw new BusinessRuleException("NC_REEMBOLSO_SIN_CLIENTE",
                "No se pudo identificar al cliente de la nota de crédito; revisa su factura o anticipo de origen.");

        var ahora = _clock.UtcNow;
        nc.SolicitarReembolso(command.Importe, ahora);

        await _eventos.PublishAsync(new NotaCreditoReembolsoSolicitadoIntegrationEvent(
            EmpresaId: nc.EmpresaId, OcurridoEn: ahora, NotaCreditoId: nc.Id, Folio: nc.Folio, Uuid: nc.Uuid,
            ClienteId: clienteId, ReceptorRfc: nc.ReceptorRfc, Importe: command.Importe,
            TotalNotaCredito: nc.Total, Moneda: nc.Moneda, SucursalId: nc.SucursalId,
            Motivo: command.Motivo.Trim(), SolicitadoPor: usuarioId), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return new SolicitarReembolsoNotaCreditoResponse(nc.Id, command.Importe, ahora);
    }
}
