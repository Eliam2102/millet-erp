using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Application.Movimientos;
using Millet.Tesoreria.Domain.Ports;
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
    int Version, Guid? PropuestoPor = null, decimal SaldoAFavorPorIdentificar = 0,
    Guid? MovimientoSugeridoId = null,
    decimal? SaldoPorDepositar = null,
    IReadOnlyList<DepositoFichaResponse>? Fichas = null,
    // 86e3vr1tj: solo cortes de caja. El corte no se concilia mientras su diferencia no esté autorizada
    // (el depósito sí se puede registrar). Importe con signo: negativo faltante, positivo sobrante.
    EstadoDiferenciaCorte? DiferenciaEstado = null,
    decimal? DiferenciaImporte = null,
    bool? Conciliado = null);

public sealed record DepositoFichaResponse(
    Guid Id, Guid MovimientoId, decimal Importe, DateOnly FechaFicha, string? NumeroFicha,
    SoporteFicha? SoporteTipo, string? SoporteFolio, DateTimeOffset ConfirmadaEn);

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
        DepositoConfirmacion d, string? clienteClave = null, string? clienteRazonSocial = null,
        DiferenciaCorte? diferencia = null) =>
        new(d.Id, d.PropuestaCxcId, d.CajaSesionId, d.ClienteId, clienteClave, clienteRazonSocial,
            d.DepositoRef, d.MontoEsperado, d.Moneda, d.Estado, d.MotivoRechazo, d.ReppTimbrado,
            // Pendiente: MovimientoId es solo la sugerencia de CxC (va en MovimientoSugeridoId).
            d.Estado == EstadoDepositoConfirmacion.Pendiente ? null : d.MovimientoId, ParseFacturas(d.FacturasJson), d.ResueltaPor, d.ResueltaEn,
            d.CreatedAt, d.Version, d.PropuestoPor, d.SaldoAFavorPorIdentificar, d.MovimientoSugeridoId,
            d.SaldoPorDepositar,
            d.CajaSesionId is null ? null : d.Fichas.OrderBy(f => f.ConfirmadaEn)
                .Select(f => new DepositoFichaResponse(f.Id, f.MovimientoId, f.Importe, f.FechaFicha, f.NumeroFicha,
                    f.SoporteTipo, f.SoporteFolio, f.ConfirmadaEn)).ToList(),
            diferencia?.Estado, diferencia?.Importe,
            d.CajaSesionId is null ? null : d.Estado == EstadoDepositoConfirmacion.Confirmada && (diferencia is null || diferencia.Autorizada));
}

// --------------------------------------------------- Confirmar

public sealed record ConfirmarDepositoCommand(
    Guid DepositoId,
    Guid MovimientoBancarioId,
    int VersionEsperada,
    RegistrarMovimientoIngresoCommand? Ingreso = null,
    SoporteFicha? SoporteTipo = null,
    string? SoporteFolio = null) : IRequest<DepositoConfirmacionResponse>;

public sealed class ConfirmarDepositoValidator : AbstractValidator<ConfirmarDepositoCommand>
{
    public ConfirmarDepositoValidator()
    {
        RuleFor(c => c.DepositoId).NotEmpty();
        RuleFor(c => c.MovimientoBancarioId).NotEmpty().When(c => c.Ingreso is null);
        RuleFor(c => c.MovimientoBancarioId).Empty().When(c => c.Ingreso is not null);
        RuleFor(c => c.Ingreso!).SetValidator(new RegistrarMovimientoIngresoValidator()).When(c => c.Ingreso is not null);
        RuleFor(c => c.SoporteTipo!.Value).IsInEnum().When(c => c.SoporteTipo is not null);
        RuleFor(c => c.SoporteFolio).MaximumLength(40);
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
    private readonly IPeriodoContablePort? _periodos;

    public ConfirmarDepositoHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IIntegrationEventPublisher publisher,
        IClock clock, IPeriodoContablePort? periodos = null)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _publisher = publisher; _clock = clock; _periodos = periodos;
    }

    public async Task<DepositoConfirmacionResponse> Handle(
        ConfirmarDepositoCommand command, CancellationToken cancellationToken)
    {
        // Mismo lock que solicitar/pagar devoluciones: la carrera depósito↔devolución se serializa.
        if (!_db.Database.IsRelational()) return await ConfirmarAsync(command, cancellationToken);
        DepositoConfirmacionResponse? resultado = null;
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(_db,
            Millet.Tesoreria.Application.Devoluciones.DevolucionLectura.LockKey,
            async ct => resultado = await ConfirmarAsync(command, ct), cancellationToken);
        return resultado!;
    }

    private async Task<DepositoConfirmacionResponse> ConfirmarAsync(
        ConfirmarDepositoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que confirma.");

        var deposito = await _db.DepositosConfirmacion
            .Include(d => d.Fichas)
            .FirstOrDefaultAsync(d => d.Id == command.DepositoId, cancellationToken)
            ?? throw new EntityNotFoundException("DEP_NO_ENCONTRADO",
                $"No se encontró el depósito '{command.DepositoId}'.");
        if (deposito.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(DepositoConfirmacion), deposito.Id);

        var movimiento = command.Ingreso is not null
            ? await new RegistrarMovimientoIngresoHandler(_db, _currentEmpresa, _currentUser,
                _periodos ?? throw new InvalidOperationException("Falta el puerto contable."), _clock)
                .CrearAsync(command.Ingreso, cancellationToken)
            : await _db.MovimientosBancarios.FirstOrDefaultAsync(m => m.Id == command.MovimientoBancarioId, cancellationToken)
                ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO", "No se encontró el ingreso bancario.");

        if (command.Ingreso is null && await _db.DevolucionesCliente.AnyAsync(d => d.MovimientoOrigenId == movimiento.Id
                && (d.Estado == EstadoDevolucionCliente.Solicitada || d.Estado == EstadoDevolucionCliente.Autorizada), cancellationToken))
            throw new BusinessRuleException("DEP_DEVOLUCION_EN_CURSO",
                "El ingreso tiene una devolución a cliente solicitada o autorizada. Cancélala o recházala antes de confirmar el depósito.");

        // C7.4: lo ya confirmado contra este movimiento (sobrante de una propuesta previa).
        var aplicadoPrevio = command.Ingreso is not null ? 0m
            : (await DepositoDisponible.AplicadoPorMovimientoAsync(_db, [movimiento], cancellationToken))
                .GetValueOrDefault(movimiento.Id);

        // Corte de caja: 1..N fichas, un evento por ficha (Millet 23-oct).
        if (deposito.CajaSesionId is not null)
        {
            if (command.Ingreso is null && await _db.DepositoFichas.AnyAsync(f => f.MovimientoId == movimiento.Id, cancellationToken))
                throw new BusinessRuleException("DEP_FICHA_YA_LIGADA",
                    "La ficha ya está ligada a un corte; una ficha no puede pertenecer a dos cortes.");
            var ficha = deposito.AgregarFicha(movimiento, usuarioId, _clock.UtcNow, command.SoporteTipo, command.SoporteFolio);
            if (command.Ingreso is not null) _db.MovimientosBancarios.Add(movimiento);
            _db.DepositoFichas.Add(ficha);
            await _publisher.PublishAsync(new DepositoCajaConfirmadoIntegrationEvent(
                EmpresaId: empresaId,
                OcurridoEn: _clock.UtcNow,
                DepositoId: deposito.Id,
                FichaId: ficha.Id,
                CajaSesionId: deposito.CajaSesionId.Value,
                DiaCorte: deposito.DiaCorte,
                SucursalId: deposito.SucursalId,
                CajeroId: deposito.CajeroId,
                MovimientoBancarioId: movimiento.Id,
                CuentaBancariaId: movimiento.CuentaBancariaId,
                Importe: ficha.Importe,
                Moneda: movimiento.Moneda,
                FechaFicha: ficha.FechaFicha,
                NumeroFicha: ficha.NumeroFicha,
                SoporteTipo: ficha.SoporteTipo?.ToString(),
                SoporteFolio: ficha.SoporteFolio,
                ImporteCorte: deposito.MontoEsperado ?? 0m,
                SaldoPorDepositar: deposito.SaldoPorDepositar ?? 0m,
                CorteCompleto: deposito.Estado == EstadoDepositoConfirmacion.Confirmada,
                ConfirmadaPor: usuarioId), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return DepositoMapper.ToResponse(deposito, diferencia: await _db.DiferenciasCorte.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CajaSesionId == deposito.CajaSesionId, cancellationToken));
        }

        // RN-6 + cotejo monto/moneda contra lo disponible, validados en el dominio.
        deposito.Confirmar(movimiento, usuarioId, _clock.UtcNow, DepositoDisponible.Disponible(movimiento, aplicadoPrevio));

        if (command.Ingreso is not null) _db.MovimientosBancarios.Add(movimiento);

        // La confirmación ES el documento que aplica el ingreso (§4.2). C7.4: aplica
        // lo propuesto; el saldo a favor por identificar deja el movimiento
        // AplicadoParcial y vuelve a la bandeja de depósitos sin identificar de CxC.
        movimiento.ActualizarEstadoAplicacion(aplicadoPrevio + deposito.ImporteAplicadoDe(movimiento.Monto));

        // Solo las propuestas de CxC disparan el ciclo fiscal (REPP); los
        // cortes de caja se cubren con fichas (arriba) — el mostrador timbró lo suyo.
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
                // C7.4: el monto del cobro confirmado (el depósito propuesto). Igual al del
                // movimiento salvo que este se identifique en partes (sobrante).
                Monto: deposito.MontoEsperado ?? movimiento.Monto,
                Moneda: movimiento.Moneda,
                FechaValor: movimiento.FechaValor,
                Referencia: movimiento.ReferenciaBancaria,
                ConfirmadaPor: usuarioId,
                SaldoAFavorPorIdentificar: deposito.SaldoAFavorPorIdentificar,
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

        // CxC consume este rechazo y libera la reserva para una nueva propuesta.
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
