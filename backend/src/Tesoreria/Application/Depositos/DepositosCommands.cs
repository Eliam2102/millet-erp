using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Depositos;

// ============================================================================
// TES-PR7: confirmación de cobros de cliente (§3.3, TES-9). Tesorería es
// el confirmador del hecho BANCARIO: ligar el movimiento de ingreso a la
// propuesta de CxC publica `pago-cliente.confirmado.v1` → Facturación
// emite el REPP → CxC aplica a cartera al consumir el timbrado. El
// rechazo publica `propuesta-aplicacion.rechazada.v1` [T-G7] para que CxC
// re-proponga. Confirmar ≠ timbrado: el ciclo fiscal cierra aparte con
// `repp_timbrado`.
// ============================================================================

public sealed record DepositoFacturaResponse(
    Guid FacturaVentaId,
    string? Folio,
    decimal ImporteAplicado);

public sealed record DepositoConfirmacionResponse(
    Guid Id,
    Guid? PropuestaCxcId,
    Guid? CajaSesionId,
    Guid? ClienteId,
    string? ClienteClave,
    string? ClienteRazonSocial,
    string? DepositoRef,
    decimal? MontoEsperado,
    string? Moneda,
    EstadoDepositoConfirmacion Estado,
    string? MotivoRechazo,
    bool ReppTimbrado,
    Guid? MovimientoId,
    IReadOnlyList<DepositoFacturaResponse> Facturas,
    Guid? ResueltaPor,
    DateTimeOffset? ResueltaEn,
    DateTimeOffset RecibidoEn,
    int Version);

internal static class DepositoMapper
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<DepositoFacturaResponse> ParseFacturas(string facturasJson)
    {
        try
        {
            var facturas = JsonSerializer.Deserialize<List<PropuestaFacturaPayload>>(facturasJson, JsonOpts);
            return facturas is null
                ? []
                : facturas.Select(f => new DepositoFacturaResponse(f.FacturaVentaId, f.Folio, f.ImporteAplicado)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static DepositoConfirmacionResponse ToResponse(
        DepositoConfirmacion d, string? clienteClave = null, string? clienteRazonSocial = null) =>
        new(d.Id, d.PropuestaCxcId, d.CajaSesionId, d.ClienteId, clienteClave, clienteRazonSocial,
            d.DepositoRef, d.MontoEsperado, d.Moneda, d.Estado, d.MotivoRechazo, d.ReppTimbrado,
            d.MovimientoId, ParseFacturas(d.FacturasJson), d.ResueltaPor, d.ResueltaEn,
            d.CreatedAt, d.Version);
}

// --------------------------------------------------- Confirmar

public sealed record ConfirmarDepositoCommand(
    Guid DepositoId,
    Guid MovimientoBancarioId,
    int VersionEsperada) : IRequest<DepositoConfirmacionResponse>;

public sealed class ConfirmarDepositoValidator : AbstractValidator<ConfirmarDepositoCommand>
{
    public ConfirmarDepositoValidator()
    {
        RuleFor(c => c.DepositoId).NotEmpty();
        RuleFor(c => c.MovimientoBancarioId).NotEmpty();
    }
}

public sealed class ConfirmarDepositoHandler
    : IRequestHandler<ConfirmarDepositoCommand, DepositoConfirmacionResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public ConfirmarDepositoHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _publisher = publisher; _clock = clock;
    }

    public async Task<DepositoConfirmacionResponse> Handle(
        ConfirmarDepositoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que confirma.");

        var deposito = await _db.DepositosConfirmacion
            .FirstOrDefaultAsync(d => d.Id == command.DepositoId, cancellationToken)
            ?? throw new EntityNotFoundException("DEP_NO_ENCONTRADO",
                $"No se encontró el depósito '{command.DepositoId}'.");
        if (deposito.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(DepositoConfirmacion), deposito.Id);

        var movimiento = await _db.MovimientosBancarios
            .FirstOrDefaultAsync(m => m.Id == command.MovimientoBancarioId, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO",
                $"No se encontró el movimiento bancario '{command.MovimientoBancarioId}'.");

        // RN-6 + cotejo monto/moneda validados en el dominio.
        deposito.Confirmar(movimiento, usuarioId, _clock.UtcNow);

        // La confirmación ES el documento que aplica el ingreso (§4.2):
        // el movimiento queda Aplicado y deja de ofrecerse para otra liga.
        movimiento.ActualizarEstadoAplicacion(movimiento.Monto);

        // Solo las propuestas de CxC disparan el ciclo fiscal (REPP); la
        // expectativa de Caja se liga y ya — el mostrador timbró lo suyo.
        if (deposito.PropuestaCxcId is Guid propuestaId && deposito.ClienteId is Guid clienteId)
        {
            var facturas = DepositoMapper.ParseFacturas(deposito.FacturasJson);
            await _publisher.PublishAsync(new PagoClienteConfirmadoIntegrationEvent(
                EmpresaId: empresaId,
                OcurridoEn: _clock.UtcNow,
                PropuestaId: propuestaId,
                ClienteId: clienteId,
                MovimientoBancarioId: movimiento.Id,
                CuentaBancariaId: movimiento.CuentaBancariaId,
                Monto: movimiento.Monto,
                Moneda: movimiento.Moneda,
                FechaValor: movimiento.FechaValor,
                Referencia: movimiento.ReferenciaBancaria,
                Facturas: facturas
                    .Select(f => new PagoClienteFacturaAplicada(f.FacturaVentaId, f.ImporteAplicado))
                    .ToList()), cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return DepositoMapper.ToResponse(deposito);
    }
}

// --------------------------------------------------- Rechazar

public sealed record RechazarPropuestaDepositoCommand(
    Guid DepositoId,
    string Motivo,
    int VersionEsperada) : IRequest<DepositoConfirmacionResponse>;

public sealed class RechazarPropuestaDepositoValidator : AbstractValidator<RechazarPropuestaDepositoCommand>
{
    public RechazarPropuestaDepositoValidator()
    {
        RuleFor(c => c.DepositoId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RechazarPropuestaDepositoHandler
    : IRequestHandler<RechazarPropuestaDepositoCommand, DepositoConfirmacionResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public RechazarPropuestaDepositoHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _publisher = publisher; _clock = clock;
    }

    public async Task<DepositoConfirmacionResponse> Handle(
        RechazarPropuestaDepositoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que rechaza.");

        var deposito = await _db.DepositosConfirmacion
            .FirstOrDefaultAsync(d => d.Id == command.DepositoId, cancellationToken)
            ?? throw new EntityNotFoundException("DEP_NO_ENCONTRADO",
                $"No se encontró el depósito '{command.DepositoId}'.");
        if (deposito.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(DepositoConfirmacion), deposito.Id);

        deposito.Rechazar(command.Motivo, usuarioId, _clock.UtcNow);

        // [T-G7] PLATFORM-TODO(<PropuestaRechazadaConsumerCxC>): CxC aún no
        // consume tesoreria-events; el evento se publica desde ya para que
        // el wiring del lado CxC solo requiera su listener. Mientras tanto
        // el rechazo se refleja en CxC vía su endpoint interino A2.
        if (deposito.PropuestaCxcId is Guid propuestaId)
        {
            await _publisher.PublishAsync(new PropuestaAplicacionRechazadaIntegrationEvent(
                EmpresaId: empresaId,
                OcurridoEn: _clock.UtcNow,
                PropuestaId: propuestaId,
                ClienteId: deposito.ClienteId,
                Motivo: command.Motivo.Trim(),
                RechazadaPor: usuarioId), cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return DepositoMapper.ToResponse(deposito);
    }
}
