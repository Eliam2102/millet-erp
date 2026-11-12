using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Npgsql;

namespace Millet.CuentasPorCobrar.Application.Liberacion;

// ============================================================================
// M-238 (ClickUp 86e4084bu) — corregir y reevaluar un pedido retenido.
//
// La decisión es inmutable: la reevaluación es una decisión NUEVA con la misma cascada serie → crédito → override
// (DecidirLiberacionHandler.EvaluarAsync, una sola fuente), sin autorización consumible. La corrección se hace donde
// corresponde (el cliente aquí; la condición y el saldo mal aplicado en cartera, M-237) y esta acción deja el rastro:
// CorreccionLiberacion liga la evaluación de antes con la de después. Una retenida se reevalúa una vez; si sigue
// retenida, la nueva decisión se puede corregir otra vez (cadena). Supuestos a validar: usa el permiso de decidir
// liberaciones; el monto y la moneda son los del pedido original; si la reevaluación queda pendiente de autorización,
// sigue el flujo normal de C7.3.
// ============================================================================

/// <param name="ClienteIdCorregido">Obligatorio solo en <see cref="TipoCorreccionLiberacion.Cliente"/>.</param>
public sealed record ReevaluarLiberacionRetenidaCommand(
    Guid DecisionId,
    TipoCorreccionLiberacion Tipo,
    string Detalle,
    Guid? ClienteIdCorregido) : IRequest<ReevaluacionLiberacionResponse>;

public sealed record CorreccionLiberacionResponse(
    Guid Id, string PedidoRef, Guid DecisionRetenidaId, Guid DecisionReevaluadaId,
    TipoCorreccionLiberacion Tipo, string Detalle, Guid CorregidaPor, DateTimeOffset CorregidaEn);

/// <summary>Las dos evaluaciones: la retenida (antes) y la nueva (después), con la corrección que las liga.</summary>
public sealed record ReevaluacionLiberacionResponse(
    DecisionLiberacionResponse Anterior, DecisionLiberacionResponse Nueva, CorreccionLiberacionResponse Correccion);

/// <summary>Una evaluación del historial del pedido y, si salió de una corrección, esa corrección.</summary>
public sealed record EvaluacionPedidoItem(DecisionLiberacionResponse Decision, CorreccionLiberacionResponse? Correccion);

public sealed record HistorialLiberacionPedidoQuery(string PedidoRef) : IRequest<IReadOnlyList<EvaluacionPedidoItem>>;

internal static class CorreccionLiberacionMapper
{
    public static CorreccionLiberacionResponse ToResponse(CorreccionLiberacion c) =>
        new(c.Id, c.PedidoRef, c.DecisionRetenidaId, c.DecisionReevaluadaId, c.Tipo, c.Detalle, c.CorregidaPor, c.CorregidaEn);
}

public sealed class ReevaluarLiberacionRetenidaValidator : AbstractValidator<ReevaluarLiberacionRetenidaCommand>
{
    public ReevaluarLiberacionRetenidaValidator()
    {
        RuleFor(c => c.DecisionId).NotEmpty();
        RuleFor(c => c.Tipo).IsInEnum();
        RuleFor(c => c.Detalle)
            .Must(d => !string.IsNullOrWhiteSpace(d) && d.Trim().Length >= 5)
            .WithMessage("Indica qué se corrigió y por qué (mínimo 5 caracteres).")
            .MaximumLength(254);
    }
}

public sealed class ReevaluarLiberacionRetenidaHandler(
    CuentasPorCobrarDbContext db,
    IIntegrationEventPublisher eventos,
    ICurrentEmpresaContext empresa,
    ICurrentUserContext user,
    IClock clock,
    IFacturacionPedidosSinFacturarReadPort pedidos,
    IClienteReadPort clientes)
    : IRequestHandler<ReevaluarLiberacionRetenidaCommand, ReevaluacionLiberacionResponse>
{
    public async Task<ReevaluacionLiberacionResponse> Handle(
        ReevaluarLiberacionRetenidaCommand command, CancellationToken cancellationToken)
    {
        var (empresaId, usuarioId) = PendienteLiberacion.Contexto(empresa, user, "corrige");

        var retenida = await db.DecisionesLiberacion.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == command.DecisionId, cancellationToken)
            ?? throw new EntityNotFoundException("DL_NO_ENCONTRADA", $"No se encontró la decisión '{command.DecisionId}'.");

        if (retenida.Resultado != ResultadoLiberacion.Retenido)
            throw new BusinessRuleException("DL_NO_RETENIDA",
                "Solo se corrige y reevalúa un pedido retenido; esta decisión no está retenida.");

        if (await db.CorreccionesLiberacion.AnyAsync(c => c.DecisionRetenidaId == retenida.Id, cancellationToken))
            throw new ConflictException("DL_YA_REEVALUADA",
                "Esta retención ya se reevaluó; revisa la decisión más reciente del pedido.");

        if (await db.DecisionesLiberacion.AnyAsync(
                d => d.PedidoRef == retenida.PedidoRef && d.Id != retenida.Id && d.DecididoEn > retenida.DecididoEn,
                cancellationToken))
            throw new ConflictException("DL_RETENIDA_SUPERADA",
                "Hay una decisión más reciente para este pedido; corrige esa decisión en lugar de esta.");

        var clienteId = retenida.ClienteId;
        if (command.Tipo == TipoCorreccionLiberacion.Cliente)
        {
            if (command.ClienteIdCorregido is not Guid nuevo || nuevo == Guid.Empty)
                throw new BusinessRuleException("DL_CLIENTE_REQUERIDO", "Indica el cliente correcto del pedido.");
            if (nuevo == retenida.ClienteId)
                throw new BusinessRuleException("DL_CLIENTE_SIN_CAMBIO",
                    "El cliente indicado es el mismo con el que se retuvo; corrige otro dato o elige el cliente correcto.");
            if (await clientes.ObtenerAsync(nuevo, cancellationToken) is null)
                throw new BusinessRuleException("DL_CLIENTE_NO_ENCONTRADO", "El cliente indicado no existe.");
            clienteId = nuevo;
        }
        else if (command.ClienteIdCorregido is not null)
        {
            throw new BusinessRuleException("DL_CLIENTE_SOLO_EN_CORRECCION_DE_CLIENTE",
                "El cliente solo se cambia al corregir el cliente del pedido.");
        }

        // Misma cascada que una decisión normal: serie → crédito (cobertura) → override; sin autorización consumible.
        var nueva = await new DecidirLiberacionHandler(db, eventos, empresa, user, clock, pedidos).EvaluarAsync(
            new DecidirLiberacionCommand(retenida.PedidoRef, clienteId, retenida.Moneda, retenida.MontoPedido, null),
            empresaId, usuarioId, cancellationToken);

        var correccion = CorreccionLiberacion.Crear(
            empresaId, retenida.PedidoRef, retenida.Id, nueva.Id, command.Tipo, command.Detalle, usuarioId, clock.UtcNow);
        db.CorreccionesLiberacion.Add(correccion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_correccion_liberacion_retenida",
        })
        {
            // Carrera: dos personas reevaluaron la misma retención a la vez.
            throw new ConflictException("DL_YA_REEVALUADA",
                "Esta retención ya se reevaluó; revisa la decisión más reciente del pedido.", ex);
        }

        return new ReevaluacionLiberacionResponse(
            DecisionLiberacionMapper.ToResponse(retenida, reevaluadaPorId: nueva.Id),
            DecisionLiberacionMapper.ToResponse(nueva, reevaluaDecisionId: retenida.Id),
            CorreccionLiberacionMapper.ToResponse(correccion));
    }
}

/// <summary>Historial del pedido: todas sus evaluaciones, de la primera a la última, con las correcciones que las ligan.</summary>
public sealed class HistorialLiberacionPedidoHandler(CuentasPorCobrarDbContext db)
    : IRequestHandler<HistorialLiberacionPedidoQuery, IReadOnlyList<EvaluacionPedidoItem>>
{
    public async Task<IReadOnlyList<EvaluacionPedidoItem>> Handle(
        HistorialLiberacionPedidoQuery query, CancellationToken cancellationToken)
    {
        var folio = query.PedidoRef.Trim();
        var decisiones = await db.DecisionesLiberacion.AsNoTracking()
            .Where(d => d.PedidoRef == folio)
            .OrderBy(d => d.DecididoEn).ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);
        var correcciones = await db.CorreccionesLiberacion.AsNoTracking()
            .Where(c => c.PedidoRef == folio)
            .ToListAsync(cancellationToken);

        var (porRetenida, porNueva) = DecisionLiberacionMapper.Reevaluaciones(correcciones);
        var resoluciones = await DecisionLiberacionMapper.ResolucionesAsync(
            db, decisiones.Where(d => d.Resultado == ResultadoLiberacion.PendienteAutorizacion).Select(d => d.Id).ToList(),
            cancellationToken);

        return decisiones.Select(d => new EvaluacionPedidoItem(
            DecisionLiberacionMapper.ToResponse(
                d, resoluciones.TryGetValue(d.Id, out var r) ? r : null,
                porRetenida.TryGetValue(d.Id, out var p) ? p : null,
                porNueva.TryGetValue(d.Id, out var o) ? o : null),
            correcciones.Where(c => c.DecisionReevaluadaId == d.Id).Select(CorreccionLiberacionMapper.ToResponse).FirstOrDefault()))
            .ToList();
    }
}
