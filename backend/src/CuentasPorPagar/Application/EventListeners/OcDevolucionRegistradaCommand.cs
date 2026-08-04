using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.EventListeners;

/// <summary>
/// Listener del integration event <c>almacen.oc_devolucion.registrada.v1</c>
/// (F6-PR3). Crea una <see cref="Domain.NotaCargo.NotaCargo"/> en
/// Borrador asociada a la devolución para que el operador la autorice
/// y aplique. La NC fiscal del proveedor (tipo CFDI 03) cierra el
/// ciclo formalizando la nota de cargo y publicando
/// <c>NotaCreditoFiscalDevolucionRecibidaEvent</c> a Almacén.
///
/// <para>
/// <b>Idempotencia</b>: dedupe vía <see cref="EventoProcesado"/> por
/// <c>(EventId, EventType)</c>. Re-entregas at-least-once de Service
/// Bus no crean nota de cargo duplicada.
/// </para>
///
/// <para>
/// <b>Sucursal</b>: el payload no la trae — la nota nace con
/// SucursalId = null y se completa al autorizar si aplica.
/// </para>
/// </summary>
public sealed record OcDevolucionRegistradaCommand(
    Guid EventId,
    OcDevolucionRegistradaPayload Payload) : IRequest;

public sealed class OcDevolucionRegistradaHandler : IRequestHandler<OcDevolucionRegistradaCommand>
{
    public const string EventType = "almacen.oc_devolucion.registrada.v1";

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<OcDevolucionRegistradaHandler> _logger;

    public OcDevolucionRegistradaHandler(
        CuentasPorPagarDbContext db,
        IClock clock,
        ILogger<OcDevolucionRegistradaHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(OcDevolucionRegistradaCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup)
        {
            _logger.LogDebug(
                "[OcDevolucionRegistradaHandler] Evento {EventId} ya procesado — skip.", request.EventId);
            return;
        }

        var p = request.Payload;

        // Defense-in-depth: si ya existe una NotaCargo apuntando a esta
        // devolución (por otro camino), no creamos otra. Esto cubre el
        // caso teórico de un EventoProcesado borrado manualmente.
        var existeNcg = await _db.NotasCargo
            .AsNoTracking()
            .AnyAsync(n => n.DevolucionAProveedorId == p.DevolucionId, cancellationToken);
        if (existeNcg)
        {
            _logger.LogWarning(
                "[OcDevolucionRegistradaHandler] Ya existe NotaCargo para devolucion={DevolucionId}; solo se marca evento.",
                p.DevolucionId);

            _db.EventosProcesados.Add(new EventoProcesado(
                eventoId: request.EventId,
                eventoTipo: EventType,
                procesadoEn: _clock.UtcNow,
                detalle: $"Devolucion={p.DevolucionId} — NotaCargo ya existía"));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var ahora = _clock.UtcNow;
        var anio = (short)ahora.UtcDateTime.Year;

        // Misma secuencia atómica que CrearNotaCargoCommand (F6-PR2).
        var siguiente = await _db.Database
            .SqlQuery<int>($@"
                INSERT INTO cuentas_por_pagar.folio_secuencias_nota_cargo (empresa_id, anio, siguiente)
                VALUES ({p.EmpresaId}, {anio}, 2)
                ON CONFLICT (empresa_id, anio) DO UPDATE
                  SET siguiente = cuentas_por_pagar.folio_secuencias_nota_cargo.siguiente + 1
                RETURNING (siguiente - 1)::int AS ""Value""
            ")
            .ToListAsync(cancellationToken)
            .ContinueWith(t => t.Result.Single(), cancellationToken);

        var folio = FolioInternoNotaCargo.FromAnioSecuencial(anio, siguiente);
        var concepto = $"Devolución a proveedor — {p.FolioMovimiento} — {p.Motivo}";
        if (concepto.Length > 400) concepto = concepto[..400];

        var nota = global::Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo.Crear(
            empresaId: p.EmpresaId,
            folio: folio,
            proveedorId: p.ProveedorId,
            sucursalId: null,
            concepto: concepto,
            conceptoContableId: null,
            monto: p.MontoTotalMxn,
            moneda: "MXN",
            tipoCambio: null,
            facturaOrigenId: p.FacturaProveedorOrigenId,
            devolucionAProveedorId: p.DevolucionId,
            creadoPor: null,
            ahora: ahora);

        _db.NotasCargo.Add(nota);
        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: ahora,
            detalle: $"Devolucion={p.DevolucionId} -> NotaCargo={nota.Id} Folio={nota.Folio.Valor}"));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[OcDevolucionRegistradaHandler] NotaCargo {Folio} creada en Borrador (devolucion={DevolucionId}, monto={Monto}).",
            nota.Folio.Valor, p.DevolucionId, nota.Monto);
    }
}
