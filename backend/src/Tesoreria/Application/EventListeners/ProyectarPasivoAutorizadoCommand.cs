using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// TES-PR3: proyección del pasivo autorizado a la bandeja
// `pasivo_pendiente_pago`. La escribe SOLO este listener (y las
// aplicaciones locales de pago en PR-4) — nunca un endpoint. Upsert por
// FacturaProveedorId: una re-emisión (pasivo re-autorizado tras reversa)
// adopta el estado vigente del evento; CxP es la fuente de verdad.
// ============================================================================

public sealed record ProyectarPasivoAutorizadoCommand(
    Guid EventoId,
    PasivoAutorizadoParaPagoPayload Payload) : IRequest;

public sealed class ProyectarPasivoAutorizadoHandler : IRequestHandler<ProyectarPasivoAutorizadoCommand>
{
    public const string EventType = PasivoAutorizadoParaPagoPayload.EventType;

    private readonly TesoreriaDbContext _db;
    private readonly IClock _clock;

    public ProyectarPasivoAutorizadoHandler(TesoreriaDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task Handle(ProyectarPasivoAutorizadoCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var ahora = _clock.UtcNow;

        // GI-PR2 (doc 12): pasivos INTERNOS (reposición de caja chica,
        // préstamo de viáticos) — sin factura ni proveedor; upsert por
        // origen. Cierra PLATFORM-TODO(<PasivosInternosBandeja>).
        if (p.EsPasivoInterno)
        {
            await ProyectarInternoAsync(p, ahora, command.EventoId, cancellationToken);
            return;
        }

        // UuidCfdi viaja como string en el contrato congelado; la columna
        // local es uuid. Un valor no parseable se proyecta como null (el
        // folio fiscal sigue disponible vía CxP, autoridad del dato).
        Guid? uuidCfdi = Guid.TryParse(p.UuidCfdi, out var parsed) ? parsed : null;

        var existente = await _db.PasivosPendientesPago
            .FirstOrDefaultAsync(x => x.FacturaProveedorId == p.FacturaProveedorId, cancellationToken);

        if (existente is null)
        {
            _db.PasivosPendientesPago.Add(new PasivoPendientePago(
                empresaId: p.EmpresaId,
                facturaProveedorId: p.FacturaProveedorId,
                proveedorId: p.ProveedorId,
                ordenCompraId: p.OrdenCompraId,
                montoTotal: p.MontoTotal,
                saldoPendiente: p.SaldoPendiente,
                moneda: p.Moneda,
                tipoCambio: p.TipoCambio,
                fechaVencimiento: p.FechaVencimiento,
                uuidCfdi: uuidCfdi,
                folioProveedor: p.FolioProveedor,
                recibidoEn: ahora,
                metodoPago: p.MetodoPago));
        }
        else
        {
            existente.ActualizarDesdeEvento(
                montoTotal: p.MontoTotal,
                saldoPendiente: p.SaldoPendiente,
                moneda: p.Moneda,
                tipoCambio: p.TipoCambio,
                fechaVencimiento: p.FechaVencimiento,
                uuidCfdi: uuidCfdi,
                folioProveedor: p.FolioProveedor,
                recibidoEn: ahora,
                metodoPago: p.MetodoPago);
        }

        // Marca de idempotencia en la MISMA transacción que la proyección
        // (cuidados-infra §2.3).
        _db.EventosProcesados.Add(new EventoProcesado(
            command.EventoId, EventType, ahora,
            detalle: $"factura={p.FacturaProveedorId} saldo={p.SaldoPendiente} {p.Moneda}"));

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProyectarInternoAsync(
        PasivoAutorizadoParaPagoPayload p,
        DateTimeOffset ahora,
        Guid eventoId,
        CancellationToken cancellationToken)
    {
        // Payload interno malformado (sin beneficiario/origen) → se marca
        // procesado sin proyectar; CxP es la autoridad y puede re-emitir.
        if (p.BeneficiarioId is not Guid beneficiarioId
            || p.OrigenId is not Guid origenId
            || string.IsNullOrWhiteSpace(p.OrigenTipo)
            || string.IsNullOrWhiteSpace(p.TipoBeneficiario))
        {
            _db.EventosProcesados.Add(new EventoProcesado(
                eventoId, EventType, ahora,
                detalle: "pasivo interno sin beneficiario/origen — ignorado"));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Para internos la clave física reutiliza FacturaProveedorId =
        // OrigenId (ver PasivoPendientePago.CrearInterno).
        var existente = await _db.PasivosPendientesPago
            .FirstOrDefaultAsync(x => x.FacturaProveedorId == origenId, cancellationToken);

        if (existente is null)
        {
            _db.PasivosPendientesPago.Add(Domain.Pasivos.PasivoPendientePago.CrearInterno(
                empresaId: p.EmpresaId,
                tipoBeneficiario: p.TipoBeneficiario!,
                beneficiarioId: beneficiarioId,
                origenTipo: p.OrigenTipo!,
                origenId: origenId,
                montoTotal: p.MontoTotal,
                saldoPendiente: p.SaldoPendiente,
                moneda: p.Moneda,
                fechaVencimiento: p.FechaVencimiento,
                recibidoEn: ahora));
        }
        else
        {
            existente.ActualizarDesdeEvento(
                montoTotal: p.MontoTotal,
                saldoPendiente: p.SaldoPendiente,
                moneda: p.Moneda,
                tipoCambio: null,
                fechaVencimiento: p.FechaVencimiento,
                uuidCfdi: null,
                folioProveedor: null,
                recibidoEn: ahora);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId, EventType, ahora,
            detalle: $"interno {p.OrigenTipo}={origenId} beneficiario={p.TipoBeneficiario}:{beneficiarioId} saldo={p.SaldoPendiente} {p.Moneda}"));

        await _db.SaveChangesAsync(cancellationToken);
    }
}
