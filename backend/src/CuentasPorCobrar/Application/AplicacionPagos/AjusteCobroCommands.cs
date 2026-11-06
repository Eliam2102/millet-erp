using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.AplicacionPagos;

// ============================================================================
// CXC-06 (86e3qddef): ajuste documentado de un cobro corto. Un faltante dentro
// de la tolerancia no se absorbe solo: quien propone lo documenta (importe,
// tipo = cuenta, motivo) y OTRA persona con permiso lo autoriza o lo rechaza.
// Hasta entonces la propuesta no llega a Tesorería.
// ============================================================================

public sealed record AutorizarAjustePropuestaCommand(Guid Id, int VersionEsperada) : IRequest<PropuestaAplicacionResponse>;

public sealed class AutorizarAjustePropuestaValidator : AbstractValidator<AutorizarAjustePropuestaCommand>
{
    public AutorizarAjustePropuestaValidator() => RuleFor(c => c.Id).NotEmpty();
}

public sealed class AutorizarAjustePropuestaHandler(
    CuentasPorCobrarDbContext db, IIntegrationEventPublisher eventos, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<AutorizarAjustePropuestaCommand, PropuestaAplicacionResponse>
{
    public async Task<PropuestaAplicacionResponse> Handle(AutorizarAjustePropuestaCommand command, CancellationToken cancellationToken)
    {
        if (usuario.UserId is not Guid usuarioId || usuarioId == Guid.Empty)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar a quien autoriza el ajuste.");

        var propuesta = await db.PropuestasAplicacionPago.Include(p => p.Facturas)
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PAP_NO_ENCONTRADA", "No se encontró la propuesta.");
        if (propuesta.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(PropuestaAplicacionPago), propuesta.Id);

        propuesta.AutorizarAjuste(usuarioId, clock.UtcNow);

        var ids = propuesta.Facturas.Select(f => f.FacturaCarteraId).ToList();
        var cartera = await db.FacturasCartera.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        await PropuestaAplicacionMapper.PublicarCreadaAsync(eventos, propuesta, cartera, clock.UtcNow, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await PropuestaAplicacionMapper.ToResponseAsync(db, propuesta, cancellationToken);
    }
}

public sealed record RechazarAjustePropuestaCommand(Guid Id, int VersionEsperada, string Motivo) : IRequest<PropuestaAplicacionResponse>;

public sealed class RechazarAjustePropuestaValidator : AbstractValidator<RechazarAjustePropuestaCommand>
{
    public RechazarAjustePropuestaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(370);
    }
}

/// <summary>
/// Rechazar el ajuste rechaza la propuesta: libera el saldo reservado de las facturas y quien propuso registra otra (por el depósito
/// exacto o con un ajuste corregido). Tesorería nunca la recibió, así que no hay nada que avisarle.
/// </summary>
public sealed class RechazarAjustePropuestaHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<RechazarAjustePropuestaCommand, PropuestaAplicacionResponse>
{
    public async Task<PropuestaAplicacionResponse> Handle(RechazarAjustePropuestaCommand command, CancellationToken cancellationToken)
    {
        if (usuario.UserId is not Guid usuarioId || usuarioId == Guid.Empty)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar a quien rechaza el ajuste.");

        var propuesta = await db.PropuestasAplicacionPago.Include(p => p.Facturas)
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PAP_NO_ENCONTRADA", "No se encontró la propuesta.");
        if (propuesta.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(PropuestaAplicacionPago), propuesta.Id);
        if (!propuesta.AjustePorAutorizar)
            throw new BusinessRuleException("PAP_SIN_AJUSTE_POR_AUTORIZAR",
                "Solo se rechaza aquí una propuesta con ajuste pendiente de autorización.");

        propuesta.Rechazar(usuarioId, $"Ajuste rechazado: {command.Motivo.Trim()}", clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await PropuestaAplicacionMapper.ToResponseAsync(db, propuesta, cancellationToken);
    }
}
