using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.EventListeners;

/// <summary>
/// Handler MediatR del evento
/// <c>cuentas_por_pagar.factura.diferencia-precio-detectada.v1</c>
/// (F3-PR1, A11 del 01-diseno).
///
/// <para>
/// <b>Lógica del ajuste (A11)</b>: cuando la factura llega con precio
/// diferente al de la OC y CxP lo detecta dentro de tolerancia, publica
/// este evento. Almacén:
/// <list type="bullet">
///   <item>Calcula la diferencia × cantidad remanente en stock (cantidad
///   actual del saldo, no la total recibida — el material consumido
///   ya cargó al costo viejo y no se re-valoriza retroactivamente).</item>
///   <item>Genera movimiento <c>AjustePrecioFactura</c> con monto = delta.
///   Si delta > 0 → ajuste positivo (encarece); si delta &lt; 0 →
///   ajuste negativo (abarata). Aquí el delta es el incremento del costo
///   unitario; la cantidad del movimiento de ajuste es la cantidad
///   remanente (no se mueve material, solo se valoriza).</item>
///   <item>Publica <see cref="EntradaInventarioValoradaIntegrationEvent"/>
///   con <c>EsAjuste=true</c> para que Contabilidad genere póliza.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Limitación F3-PR1</b>: el ajuste de costo del saldo se modela como
/// un movimiento aparte; NO modifica retroactivamente
/// <c>saldos_inventario.costo_promedio_mxn</c>. Una iteración futura
/// puede agregar el recálculo del promedio en el mismo movimiento si
/// Finanzas lo requiere (decisión documentada en 01-diseno §3 A11).
/// </para>
/// </summary>
public sealed record DiferenciaPrecioFacturaDetectadaCommand(
    Guid EventId,
    DiferenciaPrecioFacturaDetectadaPayload Payload) : IRequest;

public sealed class DiferenciaPrecioFacturaDetectadaHandler
    : IRequestHandler<DiferenciaPrecioFacturaDetectadaCommand>
{
    public const string EventType = "cuentas_por_pagar.factura.diferencia-precio-detectada.v1";

    private readonly AlmacenDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly ILogger<DiferenciaPrecioFacturaDetectadaHandler> _logger;

    public DiferenciaPrecioFacturaDetectadaHandler(
        AlmacenDbContext db,
        IIntegrationEventPublisher events,
        ILogger<DiferenciaPrecioFacturaDetectadaHandler> logger)
    {
        _db = db;
        _events = events;
        _logger = logger;
    }

    public async Task Handle(
        DiferenciaPrecioFacturaDetectadaCommand request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;

        // 1. Encuentra recepciones de la OC para este artículo (cualquier
        //    sub-almacén). Si no hay, ignora.
        var recepciones = await _db.Movimientos
            .Include(m => m.Lineas)
            .Where(m => m.Tipo == TipoMovimiento.EntradaCompra
                && m.OcId == payload.OrdenCompraId
                && m.Estado == EstadoMovimiento.Registrado)
            .ToListAsync(cancellationToken);

        var recepcionConArticulo = recepciones
            .FirstOrDefault(m => m.Lineas.Any(l => l.ArticuloId == payload.ArticuloId));
        if (recepcionConArticulo is null)
        {
            _logger.LogDebug(
                "DiferenciaPrecio: OC {OcId} sin recepción del artículo {ArticuloId}. Ignorando.",
                payload.OrdenCompraId, payload.ArticuloId);
            return;
        }

        // 2. Cantidad remanente en stock para (sub_almacen, articulo).
        //    PR6a: el sub-almacén salió de la cabecera; se deriva de la línea
        //    vía la vista v_movimiento_sub_almacen (una fila por movimiento).
        var subAlmacenId = await _db.MovimientosSubAlmacen.AsNoTracking()
            .Where(v => v.MovimientoId == recepcionConArticulo.Id)
            .Select(v => v.SubAlmacenId)
            .FirstAsync(cancellationToken);
        // C7.2a: remanente = SUMA sobre los bins del sub-almacén (con N bins
        // una sola fila sería parcial y el monto contable saldría mal). Sin
        // cambio de comportamiento con la ÚNICA única.
        var cantidadRemanente = await _db.SaldosInventario.AsNoTracking()
            .Where(s => s.SubAlmacenId == subAlmacenId && s.ArticuloId == payload.ArticuloId)
            .SumAsync(s => s.Cantidad, cancellationToken);

        if (cantidadRemanente <= 0)
        {
            _logger.LogInformation(
                "DiferenciaPrecio: saldo de articulo {ArticuloId} en sub-almacen {SubAlmacenId} es 0; sin ajuste de inventario remanente.",
                payload.ArticuloId, subAlmacenId);
            _db.Set<EventoProcesado>().Add(new EventoProcesado(
                request.EventId, EventType,
                $"OC={payload.OrdenCompraId} articulo={payload.ArticuloId} sin remanente"));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // 3. Generar movimiento AjustePrecioFactura. Cantidad del
        //    movimiento = cantidad_remanente. Costo unitario = diferencia
        //    unitaria (positiva o negativa). El monto total = delta * cant.
        //
        //    OJO: el agregado MovimientoInventario exige cantidad > 0 y
        //    costo >= 0 en LineaMovimiento (invariantes del dominio,
        //    F2-PR1). Para representar el ajuste, el costo se modela
        //    como valor absoluto y el SIGNO real lo determinará la
        //    proyección contable (EsAjuste=true marca la diferencia).
        //
        //    El tipo `AjustePrecioFactura` actúa como entrada (suma al
        //    saldo en el trigger PG) — pero esto distorsionaría la
        //    cantidad. Por eso, F3-PR1 publica el evento al outbox para
        //    Contabilidad pero NO genera movimiento físico de inventario.
        //    La re-valoración del costo promedio queda pendiente para
        //    una iteración futura cuando se decida si el saldo se
        //    re-valoriza o se trata como gasto financiero (01-diseno §3 A11).
        var costoUnitarioAjuste = Math.Abs(payload.DiferenciaUnitarioMxn);
        var montoAjuste = Math.Round(cantidadRemanente * payload.DiferenciaUnitarioMxn, 2);

        var empresaId = recepcionConArticulo.EmpresaId;
        var ocurrioEn = DateTimeOffset.UtcNow;

        await _events.PublishAsync(new EntradaInventarioValoradaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: ocurrioEn,
            RecepcionId: recepcionConArticulo.Id,
            OrdenCompraId: payload.OrdenCompraId,
            MontoTotalMxn: montoAjuste,
            EsAjuste: true,
            ConceptoContableSugerido: "VARIACION_PRECIO_INVENTARIO",
            Lineas: new List<LineaValoradaPayload>
            {
                new(
                    LineaRecepcionId: recepcionConArticulo.Lineas
                        .First(l => l.ArticuloId == payload.ArticuloId).Id,
                    ArticuloId: payload.ArticuloId,
                    Cantidad: cantidadRemanente,
                    CostoUnitarioMxn: costoUnitarioAjuste,
                    MontoLineaMxn: montoAjuste),
            }), cancellationToken);

        _db.Set<EventoProcesado>().Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            observaciones: $"OC={payload.OrdenCompraId} articulo={payload.ArticuloId} delta={payload.DiferenciaUnitarioMxn:F4} remanente={cantidadRemanente:F4}"));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "DiferenciaPrecio procesada. OC={OcId} Articulo={ArticuloId} DeltaUnit={Delta} CantRemanente={Cant} MontoAjuste={Monto}",
            payload.OrdenCompraId, payload.ArticuloId,
            payload.DiferenciaUnitarioMxn, cantidadRemanente, montoAjuste);
    }
}
