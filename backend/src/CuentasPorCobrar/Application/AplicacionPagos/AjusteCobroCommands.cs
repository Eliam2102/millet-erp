using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.Adjuntos;
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

/// <summary>
/// M-326 parte 2 (c): todo redondeo requiere la autorización del Gerente de Área (respuesta de Millet 11-nov). Se exige el permiso
/// <c>aplicacion-pago.autorizar-redondeo</c> además del de confirmar; sin el puerto de permisos se trata como sin permiso (falla cerrada).
/// </summary>
internal static class RedondeoCobroAutorizacion
{
    public static async Task ExigirAsync(PropuestaAplicacionPago propuesta, ICurrentUserPermissions? permisos, string accion, CancellationToken ct)
    {
        if (!propuesta.RequiereGerenteDeArea) return;
        if (permisos is null || !await permisos.TieneAsync(Millet.Identidad.Domain.PermisosCanonicos.CuentasPorCobrarAplicacionPagoAutorizarRedondeo, ct))
            throw new ForbiddenException("PAP_REDONDEO_REQUIERE_GERENTE",
                $"El redondeo lo debe {accion} el Gerente de Área: pide a quien tenga ese permiso que lo haga.");
    }
}

public sealed record AutorizarAjustePropuestaCommand(Guid Id, int VersionEsperada) : IRequest<PropuestaAplicacionResponse>;

public sealed class AutorizarAjustePropuestaValidator : AbstractValidator<AutorizarAjustePropuestaCommand>
{
    public AutorizarAjustePropuestaValidator() => RuleFor(c => c.Id).NotEmpty();
}

/// <summary>
/// M-240: la comisión bancaria se autoriza solo con su soporte adjunto (estado de cuenta o aviso del banco). Sin el puerto de adjuntos
/// cableado se trata como sin soporte (falla cerrada).
/// </summary>
public sealed class AutorizarAjustePropuestaHandler(
    CuentasPorCobrarDbContext db, IIntegrationEventPublisher eventos, ICurrentUserContext usuario, IClock clock,
    ISoporteAjusteCobroReadPort? soporte = null, ICurrentUserPermissions? permisos = null)
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

        await RedondeoCobroAutorizacion.ExigirAsync(propuesta, permisos, "autorizar", cancellationToken);
        var tieneSoporte = propuesta.TipoAjuste == TipoAjusteCobro.ComisionBancaria && soporte is not null
            && await soporte.TieneSoporteAsync(propuesta.Id, cancellationToken);
        propuesta.AutorizarAjuste(usuarioId, clock.UtcNow, tieneSoporte);

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
public sealed class RechazarAjustePropuestaHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock, ICurrentUserPermissions? permisos = null)
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

        await RedondeoCobroAutorizacion.ExigirAsync(propuesta, permisos, "rechazar", cancellationToken);
        propuesta.Rechazar(usuarioId, $"Ajuste rechazado: {command.Motivo.Trim()}", clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await PropuestaAplicacionMapper.ToResponseAsync(db, propuesta, cancellationToken);
    }
}

// ============================================================================
// M-240 (86e4083m7): reversa de un ajuste ya autorizado y contabilizado. Regresa
// el faltante a la(s) factura(s) (movimiento de cartera ReversaAjusteCobro sobre
// el REP del cobro) y pide a Contabilidad la póliza contraria, ligada a la del
// pago. Todo o nada: si Contabilidad no puede registrarla (periodo cerrado, cobro
// sin póliza), la cartera no cambia.
// ============================================================================

public sealed record RevertirAjustePropuestaCommand(Guid Id, int VersionEsperada, string Motivo) : IRequest<PropuestaAplicacionResponse>;

public sealed class RevertirAjustePropuestaValidator : AbstractValidator<RevertirAjustePropuestaCommand>
{
    public RevertirAjustePropuestaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RevertirAjustePropuestaHandler(
    CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock, IReversaAjusteCobroContablePort contabilidad)
    : IRequestHandler<RevertirAjustePropuestaCommand, PropuestaAplicacionResponse>
{
    public async Task<PropuestaAplicacionResponse> Handle(RevertirAjustePropuestaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (usuario.UserId is not Guid usuarioId || usuarioId == Guid.Empty)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar a quien revierte el ajuste.");

        var propuesta = await db.PropuestasAplicacionPago.Include(p => p.Facturas)
            .FirstOrDefaultAsync(p => p.Id == command.Id, ct)
            ?? throw new EntityNotFoundException("PAP_NO_ENCONTRADA", "No se encontró la propuesta.");
        if (propuesta.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(PropuestaAplicacionPago), propuesta.Id);

        var ahora = clock.UtcNow;
        propuesta.RevertirAjuste(usuarioId, command.Motivo, ahora);
        if (propuesta.MovimientoBancarioId is null)
            throw new BusinessRuleException("PAP_COBRO_SIN_MOVIMIENTO",
                "El cobro no tiene el movimiento bancario de Tesorería: no se puede ubicar su póliza para revertir el ajuste.");
        var rep = propuesta.ReciboPagoId!.Value;

        // El faltante regresa a las facturas de la propuesta, de la última línea hacia atrás, sin pasar de lo que el REP les aplicó.
        var pagos = await db.MovimientosCartera
            .Where(m => m.OrigenComprobanteId == rep && m.Tipo == TipoMovimientoCartera.Pago && !m.Revertido)
            .ToDictionaryAsync(m => m.FacturaCarteraId, ct);
        var ids = propuesta.Facturas.Select(f => f.FacturaCarteraId).ToList();
        var facturas = await db.FacturasCartera.Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var pendiente = Math.Abs(propuesta.AjusteNoFiscal);
        foreach (var linea in propuesta.Facturas.Reverse())
        {
            if (pendiente == 0m) break;
            if (!pagos.TryGetValue(linea.FacturaCarteraId, out var pago) || !facturas.TryGetValue(linea.FacturaCarteraId, out var factura))
                continue;
            var importe = Math.Min(pendiente, Math.Min(pago.Importe, factura.MontoPagado));
            if (importe <= 0m) continue;
            factura.RevertirPago(importe);
            db.MovimientosCartera.Add(new MovimientoCartera(propuesta.EmpresaId, factura.Id, TipoMovimientoCartera.ReversaAjusteCobro,
                rep, importe, ahora));
            pendiente -= importe;
        }
        if (pendiente > 0m)
            throw new BusinessRuleException("PAP_REVERSA_SIN_PAGO_EN_CARTERA",
                "El REP del cobro ya no está aplicado en cartera (cancelado o revertido): no hay a qué regresar el faltante.");

        var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await db.SaveChangesAsync(ct);
            // Contabilidad lee la reversa por IAjusteCobroContablePort (misma conexión: ve lo guardado en esta transacción).
            var poliza = await contabilidad.RegistrarAsync(propuesta.EmpresaId, propuesta.Id, ct);
            propuesta.RegistrarPolizaReversaAjuste(poliza.Id);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
        return await PropuestaAplicacionMapper.ToResponseAsync(db, propuesta, ct);
    }
}
