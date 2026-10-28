using FluentValidation;
using MediatR;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.Liberacion;

// ============================================================================
// C7.3 (ClickUp 86e3jvv6n) — rechazar una decisión PendienteAutorizacion.
// Mismas validaciones que «Autorizar» (pendiente vigente, sin autoconsumo,
// 409 si ya se resolvió o la superó otra decisión). Emite una decisión
// NUEVA Retenido (regla Cobertura: se retiene por cobertura parcial no
// autorizada) con DecisionOrigenId = pendiente, decidida por quien
// rechaza y con su motivo. Conserva el snapshot y la cobertura de la
// pendiente (se rechaza lo que se vio); no crea autorización.
// ============================================================================

public sealed record RechazarLiberacionPendienteCommand(Guid DecisionId, string Motivo)
    : IRequest<DecisionLiberacionResponse>;

public sealed class RechazarLiberacionPendienteValidator : AbstractValidator<RechazarLiberacionPendienteCommand>
{
    public RechazarLiberacionPendienteValidator()
    {
        RuleFor(c => c.DecisionId).NotEmpty();
        RuleFor(c => c.Motivo)
            .Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("El motivo es obligatorio.")
            .MaximumLength(254);
    }
}

public sealed class RechazarLiberacionPendienteHandler
    : IRequestHandler<RechazarLiberacionPendienteCommand, DecisionLiberacionResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public RechazarLiberacionPendienteHandler(
        CuentasPorCobrarDbContext db,
        IIntegrationEventPublisher eventos,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db; _eventos = eventos; _currentEmpresa = currentEmpresa;
        _currentUser = currentUser; _clock = clock;
    }

    public async Task<DecisionLiberacionResponse> Handle(
        RechazarLiberacionPendienteCommand command, CancellationToken cancellationToken)
    {
        var (empresaId, usuarioId) = PendienteLiberacion.Contexto(_currentEmpresa, _currentUser, "rechaza");
        var pendiente = await PendienteLiberacion.CargarParaResolverAsync(
            _db, command.DecisionId, usuarioId, "rechazarla", cancellationToken);

        var decision = DecisionLiberacion.Emitir(
            empresaId: empresaId,
            pedidoRef: pendiente.PedidoRef,
            clienteId: pendiente.ClienteId,
            moneda: pendiente.Moneda,
            montoPedido: pendiente.MontoPedido,
            creditoDisponibleSnapshot: pendiente.CreditoDisponibleSnapshot,
            resultado: ResultadoLiberacion.Retenido,
            reglaAplicada: ReglaAplicadaLiberacion.Cobertura,
            overrideId: null,
            decididoPor: usuarioId,
            decididoEn: _clock.UtcNow,
            porcentajeCobertura: pendiente.PorcentajeCobertura,
            decisionOrigenId: pendiente.Id,
            motivo: command.Motivo);

        _db.DecisionesLiberacion.Add(decision);

        await _eventos.PublishAsync(DecisionLiberacionMapper.ToEvent(decision), cancellationToken);
        await PendienteLiberacion.GuardarResolucionAsync(_db, cancellationToken);

        return DecisionLiberacionMapper.ToResponse(decision);
    }
}
