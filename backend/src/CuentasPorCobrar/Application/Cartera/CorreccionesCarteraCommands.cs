using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Cartera;

// ============================================================================
// M-237 (86e4084bt · CXC-CARTERA/corregir): correcciones desde el estado de cuenta.
//  - Plazo de una factura: se aplica al momento con motivo; recalcula vencimiento y antigüedad.
//  - Reversa de un movimiento mal aplicado: la solicita quien corrige y la autoriza OTRA persona
//    (misma separación de funciones que el ajuste de cobro, #651). Supuesto «a validar»: solo
//    pagos de cobro de mostrador y notas de crédito; los pagos con REP timbrado se corrigen
//    cancelando el REP. Solo mueve cartera: la diferencia contra Facturación (C7.7) y contra la
//    póliza del cobro queda visible, no se oculta.
// ============================================================================

public sealed record CorreccionCarteraResponse(
    Guid Id, Guid FacturaCarteraId, string Folio, string Tipo, string Estado, string Motivo,
    Guid SolicitadaPorId, string? SolicitadaPorNombre, DateTimeOffset SolicitadaEn,
    Guid? DecididaPorId, string? DecididaPorNombre, DateTimeOffset? DecididaEn, string? MotivoDecision,
    int? PlazoAnteriorDias, int? PlazoNuevoDias, DateTimeOffset? VencimientoAnterior, DateTimeOffset? VencimientoNuevo,
    Guid? MovimientoCarteraId, string? MovimientoTipo, decimal? MovimientoImporte)
{
    public static CorreccionCarteraResponse De(CorreccionCartera c, string folio) => new(
        c.Id, c.FacturaCarteraId, folio, Texto(c.Tipo), Texto(c.Estado), c.Motivo,
        c.SolicitadaPorId, c.SolicitadaPorNombre, c.SolicitadaEn,
        c.DecididaPorId, c.DecididaPorNombre, c.DecididaEn, c.MotivoDecision,
        c.PlazoAnteriorDias, c.PlazoNuevoDias, c.VencimientoAnterior, c.VencimientoNuevo,
        c.MovimientoCarteraId, c.MovimientoTipo is { } t ? Texto(t) : null, c.MovimientoImporte);

    public static string Texto(TipoCorreccionCartera t) => t == TipoCorreccionCartera.Plazo ? "Plazo" : "ReversaMovimiento";
    public static string Texto(EstadoCorreccionCartera e) => e.ToString();
    public static string Texto(TipoMovimientoCartera t) => t.ToString();
}

internal static class CorreccionesUsuario
{
    public static Guid Exigir(ICurrentUserContext usuario, string accion) =>
        usuario.UserId is Guid id && id != Guid.Empty
            ? id
            : throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", $"No se pudo identificar a quien {accion}.");
}

// ── Plazo ───────────────────────────────────────────────────────────────────

public sealed record CorregirPlazoFacturaCommand(Guid FacturaCarteraId, int PlazoDias, string Motivo) : IRequest<CorreccionCarteraResponse>;

public sealed class CorregirPlazoFacturaValidator : AbstractValidator<CorregirPlazoFacturaCommand>
{
    public CorregirPlazoFacturaValidator()
    {
        RuleFor(c => c.FacturaCarteraId).NotEmpty();
        RuleFor(c => c.PlazoDias).InclusiveBetween(0, 365);
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class CorregirPlazoFacturaHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<CorregirPlazoFacturaCommand, CorreccionCarteraResponse>
{
    public async Task<CorreccionCarteraResponse> Handle(CorregirPlazoFacturaCommand command, CancellationToken cancellationToken)
    {
        var usuarioId = CorreccionesUsuario.Exigir(usuario, "corrige el plazo");
        var factura = await db.FacturasCartera.FirstOrDefaultAsync(f => f.Id == command.FacturaCarteraId, cancellationToken)
            ?? throw new EntityNotFoundException("FC_NO_ENCONTRADA", "No se encontró la factura en cartera.");

        var correccion = CorreccionCartera.RegistrarPlazo(factura.EmpresaId, factura, command.PlazoDias, command.Motivo,
            usuarioId, usuario.UserName, clock.UtcNow);
        db.CorreccionesCartera.Add(correccion);
        await db.SaveChangesAsync(cancellationToken);
        return CorreccionCarteraResponse.De(correccion, factura.Folio);
    }
}

// ── Reversa de movimiento: solicitar ────────────────────────────────────────

public sealed record SolicitarReversaMovimientoCommand(Guid MovimientoCarteraId, string Motivo) : IRequest<CorreccionCarteraResponse>;

public sealed class SolicitarReversaMovimientoValidator : AbstractValidator<SolicitarReversaMovimientoCommand>
{
    public SolicitarReversaMovimientoValidator()
    {
        RuleFor(c => c.MovimientoCarteraId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class SolicitarReversaMovimientoHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<SolicitarReversaMovimientoCommand, CorreccionCarteraResponse>
{
    public async Task<CorreccionCarteraResponse> Handle(SolicitarReversaMovimientoCommand command, CancellationToken cancellationToken)
    {
        var usuarioId = CorreccionesUsuario.Exigir(usuario, "solicita la reversa");
        var movimiento = await db.MovimientosCartera.AsNoTracking().FirstOrDefaultAsync(m => m.Id == command.MovimientoCarteraId, cancellationToken)
            ?? throw new EntityNotFoundException("MC_NO_ENCONTRADO", "No se encontró el movimiento de cartera.");
        await AsegurarReversibleAsync(db, movimiento, cancellationToken);
        if (await db.CorreccionesCartera.AnyAsync(c => c.MovimientoCarteraId == movimiento.Id
                && c.Estado == EstadoCorreccionCartera.Pendiente, cancellationToken))
            throw new BusinessRuleException("CC_YA_PENDIENTE", "El movimiento ya tiene una reversa pendiente de autorización.");

        var folio = await db.FacturasCartera.AsNoTracking().Where(f => f.Id == movimiento.FacturaCarteraId)
            .Select(f => f.Folio).FirstAsync(cancellationToken);
        var correccion = CorreccionCartera.SolicitarReversa(movimiento.EmpresaId, movimiento, command.Motivo,
            usuarioId, usuario.UserName, clock.UtcNow);
        db.CorreccionesCartera.Add(correccion);
        await db.SaveChangesAsync(cancellationToken);
        return CorreccionCarteraResponse.De(correccion, folio);
    }

    /// <summary>
    /// Supuesto «a validar»: un pago con REP timbrado no se revierte aquí (se cancela el REP y la cartera lo refleja por su evento);
    /// quedan reversibles los pagos de cobro de mostrador y las notas de crédito.
    /// </summary>
    internal static async Task AsegurarReversibleAsync(CuentasPorCobrarDbContext db, MovimientoCartera movimiento, CancellationToken ct)
    {
        if (movimiento.Revertido)
            throw new BusinessRuleException("CC_MOVIMIENTO_YA_REVERTIDO", "El movimiento ya fue revertido.");
        if (movimiento.Tipo == TipoMovimientoCartera.ReversaAjusteCobro)
            throw new BusinessRuleException("CC_MOVIMIENTO_NO_REVERSIBLE", "La reversa de un ajuste de cobro tiene su propio flujo (revertir ajuste).");
        if (movimiento.Tipo == TipoMovimientoCartera.Pago
            && await db.PropuestasAplicacionPago.AnyAsync(p => p.ReciboPagoId == movimiento.OrigenComprobanteId, ct))
            throw new BusinessRuleException("CC_PAGO_CON_REP",
                "El pago se aplicó con un complemento de pago (REP): para corregirlo cancela el REP; la cartera se revierte con esa cancelación.");
    }
}

// ── Reversa de movimiento: autorizar / rechazar ─────────────────────────────

public sealed record DecidirReversaMovimientoCommand(Guid CorreccionId, bool Autorizar, string? Motivo) : IRequest<CorreccionCarteraResponse>;

public sealed class DecidirReversaMovimientoValidator : AbstractValidator<DecidirReversaMovimientoCommand>
{
    public DecidirReversaMovimientoValidator()
    {
        RuleFor(c => c.CorreccionId).NotEmpty();
        RuleFor(c => c.Motivo).MaximumLength(400);
        RuleFor(c => c.Motivo).NotEmpty().When(c => !c.Autorizar);
    }
}

public sealed class DecidirReversaMovimientoHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<DecidirReversaMovimientoCommand, CorreccionCarteraResponse>
{
    public async Task<CorreccionCarteraResponse> Handle(DecidirReversaMovimientoCommand command, CancellationToken cancellationToken)
    {
        var usuarioId = CorreccionesUsuario.Exigir(usuario, "decide la reversa");
        var correccion = await db.CorreccionesCartera.FirstOrDefaultAsync(c => c.Id == command.CorreccionId
                && c.Tipo == TipoCorreccionCartera.ReversaMovimiento, cancellationToken)
            ?? throw new EntityNotFoundException("CC_NO_ENCONTRADA", "No se encontró la solicitud de reversa.");
        var ahora = clock.UtcNow;
        correccion.Decidir(command.Autorizar, usuarioId, usuario.UserName, command.Motivo, ahora);

        var factura = await db.FacturasCartera.FirstAsync(f => f.Id == correccion.FacturaCarteraId, cancellationToken);
        if (command.Autorizar)
        {
            var movimiento = await db.MovimientosCartera.FirstOrDefaultAsync(m => m.Id == correccion.MovimientoCarteraId, cancellationToken)
                ?? throw new EntityNotFoundException("MC_NO_ENCONTRADO", "No se encontró el movimiento de cartera.");
            await SolicitarReversaMovimientoHandler.AsegurarReversibleAsync(db, movimiento, cancellationToken);
            if (movimiento.Tipo == TipoMovimientoCartera.Pago) factura.RevertirPago(movimiento.Importe);
            else factura.RevertirNotaCredito(movimiento.Importe);
            movimiento.Revertir(ahora);
        }

        await db.SaveChangesAsync(cancellationToken);
        return CorreccionCarteraResponse.De(correccion, factura.Folio);
    }
}

// ── Consulta ────────────────────────────────────────────────────────────────

public sealed record CorreccionesCarteraQuery(Guid? ClienteId, EstadoCorreccionCartera? Estado)
    : IRequest<IReadOnlyList<CorreccionCarteraResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public string TipoDocumento => "factura_cartera";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class CorreccionesCarteraHandler(CuentasPorCobrarDbContext db)
    : IRequestHandler<CorreccionesCarteraQuery, IReadOnlyList<CorreccionCarteraResponse>>
{
    public async Task<IReadOnlyList<CorreccionCarteraResponse>> Handle(CorreccionesCarteraQuery query, CancellationToken cancellationToken)
    {
        var rows = await (from c in db.CorreccionesCartera.AsNoTracking()
                          join f in db.FacturasCartera.AsNoTracking() on c.FacturaCarteraId equals f.Id
                          where (query.ClienteId == null || f.ClienteId == query.ClienteId)
                             && (query.Estado == null || c.Estado == query.Estado)
                             && (query.DocumentosPermitidos == null || query.DocumentosPermitidos.Contains(f.Id))
                          orderby c.SolicitadaEn descending
                          select new { c, f.Folio }).Take(500).ToListAsync(cancellationToken);
        return rows.Select(r => CorreccionCarteraResponse.De(r.c, r.Folio)).ToList();
    }
}
