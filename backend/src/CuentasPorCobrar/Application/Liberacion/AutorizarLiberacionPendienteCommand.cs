using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Npgsql;

namespace Millet.CuentasPorCobrar.Application.Liberacion;

// ============================================================================
// C7.3 (ClickUp 86e3jvv6n) — autorizar una decisión PendienteAutorizacion
// (cobertura 50–99.99 %). Fuentes: cuestionario 04 R3 (autorización manual
// del depto. de CxC), Matriz I7 (consumible, un solo uso, vigencia corta,
// motivo obligatorio, sin autoconsumo), R5 (autoriza el gerente de área).
//
// Flujo (todo en una transacción):
//   1. La decisión existe, está PendienteAutorizacion y no se ha resuelto
//      (409 si ya tiene resolución o si una decisión posterior del mismo
//      pedido la dejó sin efecto — supuesto C7.3 c). Ver
//      PendienteLiberacion.CargarParaResolverAsync (compartido con Rechazar).
//   2. Quien autoriza (permiso liberacion.override en el endpoint) NO es
//      quien registró la pendiente (DL_AUTOCONSUMO).
//   3. Se RECALCULA el crédito con el mismo cálculo de Decidir
//      (CreditoLiberacion), por si cambió mientras esperaba:
//        · sin línea activa / bloqueada → Retenido (regla Credito);
//        · cobertura ≥ 100 % → Liberado (regla Credito), sin autorización;
//        · cobertura < 50 % → Retenido (regla Credito);
//        · 50–99.99 % → se crea la AutorizacionCredito (supervisor = quien
//          autoriza, beneficiario = quien registró la pendiente, ref = folio
//          del pedido, vigencia 24 h), se consume en el acto y la decisión
//          es LiberadoConOverride (regla Override).
//   4. Siempre una decisión NUEVA con DecisionOrigenId = pendiente, el
//      snapshot y porcentaje recalculados y el motivo. Índice único parcial
//      sobre decision_origen_id cubre la carrera de dos resoluciones (409).
// ============================================================================

public sealed record AutorizarLiberacionPendienteCommand(Guid DecisionId, string Motivo)
    : IRequest<DecisionLiberacionResponse>;

public sealed class AutorizarLiberacionPendienteValidator : AbstractValidator<AutorizarLiberacionPendienteCommand>
{
    public AutorizarLiberacionPendienteValidator()
    {
        RuleFor(c => c.DecisionId).NotEmpty();
        RuleFor(c => c.Motivo)
            .Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("El motivo es obligatorio.")
            .MaximumLength(254);
    }
}

public sealed class AutorizarLiberacionPendienteHandler
    : IRequestHandler<AutorizarLiberacionPendienteCommand, DecisionLiberacionResponse>
{
    /// <summary>La autorización se consume en el acto; la vigencia solo cumple la invariante ≤ 24 h.</summary>
    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(24);

    private readonly CuentasPorCobrarDbContext _db;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public AutorizarLiberacionPendienteHandler(
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
        AutorizarLiberacionPendienteCommand command, CancellationToken cancellationToken)
    {
        var (empresaId, usuarioId) = PendienteLiberacion.Contexto(_currentEmpresa, _currentUser, "autoriza");
        var pendiente = await PendienteLiberacion.CargarParaResolverAsync(
            _db, command.DecisionId, usuarioId, "autorizarla", cancellationToken);

        var ahora = _clock.UtcNow;
        var credito = await CreditoLiberacion.CalcularAsync(
            _db, pendiente.ClienteId, pendiente.Moneda, cancellationToken);

        ResultadoLiberacion resultado;
        ReglaAplicadaLiberacion regla;
        decimal? porcentaje = null;
        AutorizacionCredito? autorizacion = null;

        if (!credito.LineaActiva)
        {
            resultado = ResultadoLiberacion.Retenido;
            regla = ReglaAplicadaLiberacion.Credito;
        }
        else
        {
            porcentaje = CoberturaCredito.Porcentaje(credito.Disponible, pendiente.MontoPedido);
            switch (CoberturaCredito.Evaluar(credito.Disponible, pendiente.MontoPedido))
            {
                case TramoCobertura.Plena:
                    resultado = ResultadoLiberacion.Liberado;
                    regla = ReglaAplicadaLiberacion.Credito;
                    break;
                case TramoCobertura.Insuficiente:
                    resultado = ResultadoLiberacion.Retenido;
                    regla = ReglaAplicadaLiberacion.Credito;
                    break;
                default:
                    resultado = ResultadoLiberacion.LiberadoConOverride;
                    regla = ReglaAplicadaLiberacion.Override;
                    autorizacion = AutorizacionCredito.Crear(
                        empresaId: empresaId,
                        supervisorUsuarioId: usuarioId,
                        beneficiarioUsuarioId: pendiente.DecididoPor,
                        motivo: command.Motivo,
                        clienteOPedidoRef: pendiente.PedidoRef,
                        ahora: ahora,
                        vigencia: Vigencia);
                    break;
            }
        }

        var decision = DecisionLiberacion.Emitir(
            empresaId: empresaId,
            pedidoRef: pendiente.PedidoRef,
            clienteId: pendiente.ClienteId,
            moneda: pendiente.Moneda,
            montoPedido: pendiente.MontoPedido,
            creditoDisponibleSnapshot: credito.Disponible,
            resultado: resultado,
            reglaAplicada: regla,
            overrideId: autorizacion?.Id,
            decididoPor: usuarioId,
            decididoEn: ahora,
            porcentajeCobertura: porcentaje,
            decisionOrigenId: pendiente.Id,
            motivo: command.Motivo);

        if (autorizacion is not null)
        {
            // E10: la autorización nace con el folio de la pendiente y se
            // consume para ese mismo folio.
            autorizacion.Consumir(decision.Id, pendiente.DecididoPor, ahora, pendiente.PedidoRef);
            _db.AutorizacionesCredito.Add(autorizacion);
        }
        _db.DecisionesLiberacion.Add(decision);

        await _eventos.PublishAsync(DecisionLiberacionMapper.ToEvent(decision), cancellationToken);
        await PendienteLiberacion.GuardarResolucionAsync(_db, cancellationToken);

        return DecisionLiberacionMapper.ToResponse(decision);
    }
}

/// <summary>
/// C7.3 — validaciones comunes para resolver (autorizar o rechazar) una
/// decisión PendienteAutorizacion.
/// </summary>
internal static class PendienteLiberacion
{
    public static (Guid EmpresaId, Guid UsuarioId) Contexto(
        ICurrentEmpresaContext empresa, ICurrentUserContext usuario, string accion)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (usuario.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                $"No se pudo identificar al usuario que {accion}.");
        return (empresaId, usuarioId);
    }

    /// <param name="accion">«autorizarla» / «rechazarla», para el mensaje de autoconsumo.</param>
    public static async Task<DecisionLiberacion> CargarParaResolverAsync(
        CuentasPorCobrarDbContext db, Guid decisionId, Guid usuarioId, string accion,
        CancellationToken cancellationToken)
    {
        var pendiente = await db.DecisionesLiberacion.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == decisionId, cancellationToken)
            ?? throw new EntityNotFoundException("DL_NO_ENCONTRADA",
                $"No se encontró la decisión '{decisionId}'.");

        if (pendiente.Resultado != ResultadoLiberacion.PendienteAutorizacion)
            throw new BusinessRuleException("DL_NO_PENDIENTE",
                "Solo se resuelven decisiones pendientes de autorización (cobertura entre 50 % y 99 %).");

        if (pendiente.DecididoPor == usuarioId)
            throw new BusinessRuleException("DL_AUTOCONSUMO",
                $"Quien registró la decisión pendiente no puede {accion}; debe hacerlo otro gerente de CxC.");

        if (await db.DecisionesLiberacion.AnyAsync(
                d => d.DecisionOrigenId == pendiente.Id, cancellationToken))
            throw YaResuelta();

        if (await db.DecisionesLiberacion.AnyAsync(
                d => d.PedidoRef == pendiente.PedidoRef
                     && d.Id != pendiente.Id
                     && d.DecididoEn > pendiente.DecididoEn,
                cancellationToken))
            throw new ConflictException("DL_PENDIENTE_SUPERADA",
                "Hay una decisión más reciente para este pedido; revisa esa decisión en lugar de la pendiente.");

        return pendiente;
    }

    /// <summary>Guarda la resolución; la carrera de dos resoluciones (23505) → 409.</summary>
    public static async Task GuardarResolucionAsync(CuentasPorCobrarDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_decision_liberacion_origen",
        })
        {
            // Carrera: dos gerentes resolvieron la misma pendiente a la vez.
            throw new ConflictException("DL_PENDIENTE_YA_RESUELTA", MensajeYaResuelta, ex);
        }
    }

    private const string MensajeYaResuelta = "Esta decisión pendiente ya fue resuelta (autorizada o rechazada).";

    private static ConflictException YaResuelta() =>
        new("DL_PENDIENTE_YA_RESUELTA", MensajeYaResuelta);
}
