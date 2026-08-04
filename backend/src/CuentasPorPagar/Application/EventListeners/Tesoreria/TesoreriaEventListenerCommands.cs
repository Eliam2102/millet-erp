using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.EventListeners.Tesoreria;

// ============================================================================
// F9-PR1: handlers de los 4 eventos que Tesorería emite y CxP suscribe.
// Idempotencia: dedupe vía EventoProcesado por (EventoId, EventoTipo).
// ============================================================================

// ---------------------------------------- 1. Pago aplicado

public sealed record PagoFacturaProveedorAplicadoCommand(
    Guid EventId,
    PagoFacturaProveedorPayload Payload) : IRequest;

public sealed class PagoFacturaProveedorAplicadoHandler : IRequestHandler<PagoFacturaProveedorAplicadoCommand>
{
    public const string EventType = "tesoreria.pago-factura-proveedor.aplicado.v1";

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;
    private readonly ILogger<PagoFacturaProveedorAplicadoHandler> _logger;

    public PagoFacturaProveedorAplicadoHandler(
        CuentasPorPagarDbContext db, IClock clock, IPublisher publisher,
        ILogger<PagoFacturaProveedorAplicadoHandler> logger)
    {
        _db = db; _clock = clock; _publisher = publisher; _logger = logger;
    }

    public async Task Handle(PagoFacturaProveedorAplicadoCommand request, CancellationToken cancellationToken)
    {
        if (await YaProcesado(request.EventId, EventType, cancellationToken))
        {
            _logger.LogDebug("[PagoFacturaAplicado] {EventId} ya procesado — skip.", request.EventId);
            return;
        }

        var p = request.Payload;
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == p.FacturaProveedorId, cancellationToken);
        if (factura is null)
        {
            _logger.LogWarning(
                "[PagoFacturaAplicado] Factura {FacturaId} no existe — registrando evento como procesado para evitar retries.",
                p.FacturaProveedorId);
            await RegistrarProcesado(request.EventId, EventType,
                $"Factura {p.FacturaProveedorId} no encontrada", cancellationToken);
            return;
        }

        factura.RegistrarPago(
            monto: p.Monto,
            ahora: _clock.UtcNow,
            observacion: $"Pago Tesorería {p.PagoId} ref={p.ReferenciaBancaria ?? "-"}");

        // Propaga el acumulado pagado hacia Compras (sub-estado Pago de la
        // OC → cierre automático). El mapper publica al outbox en la misma
        // TX que el SaveChanges de RegistrarProcesado.
        await _publisher.Publish(new FacturaProveedorPagoAplicadoDomainEvent(
            EmpresaId: factura.EmpresaId,
            FacturaProveedorId: factura.Id,
            OrdenCompraId: factura.OrdenCompraId,
            ImportePagadoFactura: factura.ImportePagado,
            OcurridoEn: _clock.UtcNow), cancellationToken);

        await RegistrarProcesado(request.EventId, EventType,
            $"Factura={p.FacturaProveedorId} monto={p.Monto} pago={p.PagoId}", cancellationToken);

        _logger.LogInformation(
            "[PagoFacturaAplicado] Factura {FacturaId} actualizada: pagado={Pagado} saldo={Saldo} estado={Estado}",
            factura.Id, factura.ImportePagado, factura.SaldoPendiente, factura.Estado);
    }

    private Task<bool> YaProcesado(Guid eventId, string tipo, CancellationToken ct) =>
        _db.EventosProcesados.AsNoTracking()
            .AnyAsync(e => e.EventoId == eventId && e.EventoTipo == tipo, ct);

    private async Task RegistrarProcesado(Guid eventId, string tipo, string detalle, CancellationToken ct)
    {
        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: eventId,
            eventoTipo: tipo,
            procesadoEn: _clock.UtcNow,
            detalle: detalle));
        await _db.SaveChangesAsync(ct);
    }
}

// ---------------------------------------- 2. Pago revertido

public sealed record PagoFacturaProveedorRevertidoCommand(
    Guid EventId,
    PagoFacturaProveedorRevertidoPayload Payload) : IRequest;

public sealed class PagoFacturaProveedorRevertidoHandler : IRequestHandler<PagoFacturaProveedorRevertidoCommand>
{
    public const string EventType = "tesoreria.pago-factura-proveedor.revertido.v1";

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;
    private readonly ILogger<PagoFacturaProveedorRevertidoHandler> _logger;

    public PagoFacturaProveedorRevertidoHandler(
        CuentasPorPagarDbContext db, IClock clock, IPublisher publisher,
        ILogger<PagoFacturaProveedorRevertidoHandler> logger)
    {
        _db = db; _clock = clock; _publisher = publisher; _logger = logger;
    }

    public async Task Handle(PagoFacturaProveedorRevertidoCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados.AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        var p = request.Payload;
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == p.FacturaProveedorId, cancellationToken);
        if (factura is not null)
        {
            factura.RevertirPago(
                monto: p.MontoRevertido,
                ahora: _clock.UtcNow,
                observacion: $"Reverso pago Tesorería ({p.Motivo})");

            // Propaga el acumulado reducido hacia Compras: RegistrarPago
            // es un set — la OC baja su sub-estado Pago y reabre si estaba
            // Cerrada (F5-PR1 ya lo soporta).
            await _publisher.Publish(new FacturaProveedorPagoAplicadoDomainEvent(
                EmpresaId: factura.EmpresaId,
                FacturaProveedorId: factura.Id,
                OrdenCompraId: factura.OrdenCompraId,
                ImportePagadoFactura: factura.ImportePagado,
                OcurridoEn: _clock.UtcNow), cancellationToken);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: _clock.UtcNow,
            detalle: $"Factura={p.FacturaProveedorId} monto_revertido={p.MontoRevertido}"));
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ---------------------------------------- 3. Repp recibido

public sealed record ReppProveedorRecibidoCommand(
    Guid EventId,
    ReppProveedorRecibidoPayload Payload) : IRequest;

public sealed class ReppProveedorRecibidoHandler : IRequestHandler<ReppProveedorRecibidoCommand>
{
    public const string EventType = "tesoreria.repp-proveedor.recibido.v1";

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ReppProveedorRecibidoHandler> _logger;

    public ReppProveedorRecibidoHandler(
        CuentasPorPagarDbContext db, IClock clock, ILogger<ReppProveedorRecibidoHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(ReppProveedorRecibidoCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados.AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        var p = request.Payload;
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == p.FacturaProveedorId, cancellationToken);
        if (factura is not null)
        {
            factura.MarcarReppRecibido();
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: _clock.UtcNow,
            detalle: $"Factura={p.FacturaProveedorId} uuid_repp={p.UuidComplementoPago}"));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ReppProveedorRecibido] Factura {FacturaId} marcada con REPP {Uuid}.",
            p.FacturaProveedorId, p.UuidComplementoPago);
    }
}

// ---------------------------------------- 4. Cancelación solicitada

public sealed record CancelacionPasivoSolicitadaCommand(
    Guid EventId,
    CancelacionPasivoSolicitadaPayload Payload) : IRequest;

/// <summary>
/// Tesorería solicita cancelar el pasivo (ej. NC fiscal posterior).
/// CxP marca la factura para revisión con un motivo dedicado; el
/// Auxiliar decide si cancelar o no via el flujo normal.
///
/// <para>
/// MVP F9-PR1: el listener loguea + registra el evento como procesado;
/// la transición a EnRevision con el motivo "TESORERIA_SOLICITA_CANCELAR"
/// requiere conocer la dependencia revisora (CxP misma). Como el seed
/// del motivo + la dependencia se introducen en este PR, el handler
/// puede invocar EnviarARevision directamente.
/// </para>
/// </summary>
public sealed class CancelacionPasivoSolicitadaHandler : IRequestHandler<CancelacionPasivoSolicitadaCommand>
{
    public const string EventType = "tesoreria.cancelacion-pasivo.solicitada.v1";

    // GUID determinista del motivo nuevo (seed en la migración F9-PR1).
    public static readonly Guid MotivoTesoreriaSolicitaCancelarId =
        Guid.Parse("00000007-1001-0000-0000-000000000099");

    // Dependencia revisora "CxP" (placeholder genérico; cuando exista
    // catálogo real de dependencias en DatosMaestros, se reemplaza).
    public static readonly Guid DependenciaCxpId =
        Guid.Parse("00000007-1002-0000-0000-000000000001");

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<CancelacionPasivoSolicitadaHandler> _logger;

    public CancelacionPasivoSolicitadaHandler(
        CuentasPorPagarDbContext db, IClock clock, ILogger<CancelacionPasivoSolicitadaHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(CancelacionPasivoSolicitadaCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados.AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        var p = request.Payload;
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == p.FacturaProveedorId, cancellationToken);
        if (factura is not null
            && factura.Estado is Domain.FacturaProveedor.EstadoPasivo.Capturada
                                  or Domain.FacturaProveedor.EstadoPasivo.Autorizada)
        {
            factura.EnviarARevision(
                motivoRevisionId: MotivoTesoreriaSolicitaCancelarId,
                dependenciaRevisoraId: DependenciaCxpId,
                usuarioId: p.UsuarioSolicitanteId,
                ahora: _clock.UtcNow);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: _clock.UtcNow,
            detalle: $"Factura={p.FacturaProveedorId} motivo={p.Motivo}"));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[CancelacionPasivoSolicitada] Factura {FacturaId} marcada para revisión por Tesorería.",
            p.FacturaProveedorId);
    }
}

// ---------------------------------------- 5. Pago de préstamo de viáticos (GI-PR3)

public sealed record PagoPrestamoViaticosAplicadoCommand(
    Guid EventId,
    PagoPrestamoViaticosAplicadoPayload Payload) : IRequest;

/// <summary>
/// GI-PR3 (doc 12 §D3-vuelta): Tesorería pagó el préstamo de viáticos —
/// la solicitud transiciona a <c>Anticipada</c> automáticamente. Cierra
/// PLATFORM-TODO(&lt;TesoreriaPagoViaticosEvent&gt;); el endpoint
/// <c>marcar-pagado</c> queda como fallback manual (Q2): si alguien lo
/// usó antes de que llegara el evento, la solicitud ya está Anticipada
/// y el evento se registra como procesado sin error.
/// </summary>
public sealed class PagoPrestamoViaticosAplicadoHandler : IRequestHandler<PagoPrestamoViaticosAplicadoCommand>
{
    public const string EventType = "tesoreria.pago-prestamo-viaticos.aplicado.v1";

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<PagoPrestamoViaticosAplicadoHandler> _logger;

    public PagoPrestamoViaticosAplicadoHandler(
        CuentasPorPagarDbContext db, IClock clock,
        ILogger<PagoPrestamoViaticosAplicadoHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(PagoPrestamoViaticosAplicadoCommand request, CancellationToken cancellationToken)
    {
        var yaProcesado = await _db.EventosProcesados.AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (yaProcesado)
        {
            _logger.LogDebug("[PagoPrestamoViaticos] {EventId} ya procesado — skip.", request.EventId);
            return;
        }

        var p = request.Payload;
        var solicitud = await _db.SolicitudesViaticos
            .FirstOrDefaultAsync(s => s.Id == p.SolicitudViaticosId, cancellationToken);

        string detalle;
        if (solicitud is null)
        {
            detalle = $"Solicitud {p.SolicitudViaticosId} no encontrada — ignorado";
            _logger.LogWarning(
                "[PagoPrestamoViaticos] Solicitud {SolicitudId} no existe — registrando como procesado.",
                p.SolicitudViaticosId);
        }
        else if (solicitud.Estado is Domain.Viaticos.EstadoSolicitudViaticos.AutorizadaPorJefe
                 or Domain.Viaticos.EstadoSolicitudViaticos.AutorizadaCompleta)
        {
            solicitud.MarcarAnticipoPagado(_clock.UtcNow);
            detalle = $"Solicitud={solicitud.Id} → Anticipada; pago={p.PagoId} monto={p.MontoPagado} ref={p.ReferenciaBancaria ?? "-"}";
            _logger.LogInformation(
                "[PagoPrestamoViaticos] Solicitud {SolicitudId} → Anticipada (pago {PagoId}, {Monto} {Moneda}).",
                solicitud.Id, p.PagoId, p.MontoPagado, p.Moneda);
        }
        else
        {
            // Ya Anticipada (fallback manual marcar-pagado ganó la carrera)
            // u otro estado — idempotencia semántica, sin error.
            detalle = $"Solicitud={solicitud.Id} en estado {solicitud.Estado} — sin transición";
            _logger.LogInformation(
                "[PagoPrestamoViaticos] Solicitud {SolicitudId} en {Estado} — evento registrado sin transición.",
                solicitud.Id, solicitud.Estado);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: _clock.UtcNow,
            detalle: detalle));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
