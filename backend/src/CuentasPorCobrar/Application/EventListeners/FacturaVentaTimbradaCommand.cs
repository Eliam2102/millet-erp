using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.EventListeners;

/// <summary>
/// Listener de <c>facturacion.factura-venta.timbrada.v1</c> (CXC-PR3).
/// Alta en la proyección <c>factura_cartera</c>: resuelve el cliente por
/// RFC vía <see cref="IClienteReadPort"/> y calcula el vencimiento con el
/// plazo de la línea de crédito activa del cliente en la moneda de la
/// factura (sin línea → vence al timbrado, i.e. contado).
///
/// <para>
/// <b>Idempotencia doble</b>: dedupe por <see cref="EventoProcesado"/>
/// (re-entrega del mismo mensaje) + upsert por <c>FacturaVentaId</c>
/// único (re-emisión del evento con otro MessageId, p.ej. reintento de
/// timbrado). En el upsert solo se re-vincula el cliente si antes no
/// había match de RFC.
/// </para>
/// </summary>
public sealed record FacturaVentaTimbradaCommand(
    Guid EventId,
    FacturaVentaTimbradaPayload Payload) : IRequest;

public sealed class FacturaVentaTimbradaHandler : IRequestHandler<FacturaVentaTimbradaCommand>
{
    public const string EventType = "facturacion.factura-venta.timbrada.v1";

    private readonly CuentasPorCobrarDbContext _db;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;
    private readonly ILogger<FacturaVentaTimbradaHandler> _logger;

    public FacturaVentaTimbradaHandler(
        CuentasPorCobrarDbContext db,
        IClienteReadPort clientes,
        IClock clock,
        ILogger<FacturaVentaTimbradaHandler> logger)
    {
        _db = db; _clientes = clientes; _clock = clock; _logger = logger;
    }

    public async Task Handle(FacturaVentaTimbradaCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup)
        {
            _logger.LogDebug("[FacturaVentaTimbrada] Evento {EventId} ya procesado — skip.", request.EventId);
            return;
        }

        var p = request.Payload;

        var cliente = string.IsNullOrWhiteSpace(p.ReceptorRfc)
            ? null
            : await _clientes.ObtenerPorRfcAsync(p.ReceptorRfc, cancellationToken);

        var existente = await _db.FacturasCartera
            .FirstOrDefaultAsync(f => f.FacturaVentaId == p.FacturaVentaId, cancellationToken);

        if (existente is null)
        {
            var fechaTimbrado = p.FechaTimbrado ?? p.OcurridoEn;
            var fechaVencimiento = fechaTimbrado;

            if (cliente is not null)
            {
                var linea = await _db.LineasCredito.AsNoTracking()
                    .FirstOrDefaultAsync(l => l.ClienteId == cliente.Id
                                           && l.Moneda == p.Moneda
                                           && l.Estado == EstadoLineaCredito.Activa,
                        cancellationToken);
                if (linea is not null)
                    fechaVencimiento = fechaTimbrado.AddDays(linea.PlazoDias);
            }

            _db.FacturasCartera.Add(FacturaCartera.Crear(
                empresaId: p.EmpresaId,
                facturaVentaId: p.FacturaVentaId,
                clienteId: cliente?.Id,
                receptorRfc: p.ReceptorRfc,
                receptorNombre: p.ReceptorNombre,
                uuid: p.Uuid,
                folio: p.Folio,
                total: p.Total,
                moneda: p.Moneda,
                metodoPago: p.MetodoPago,
                fechaTimbrado: fechaTimbrado,
                fechaVencimiento: fechaVencimiento));
        }
        else if (existente.ClienteId is null && cliente is not null)
        {
            existente.VincularCliente(cliente.Id);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: _clock.UtcNow,
            detalle: $"Factura={p.FacturaVentaId} Folio={p.Folio} Rfc={p.ReceptorRfc} Cliente={(cliente?.Id.ToString() ?? "sin-match")}"));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[FacturaVentaTimbrada] Factura {Folio} ({Total} {Moneda}) proyectada en cartera. Cliente={Cliente}.",
            p.Folio, p.Total, p.Moneda, cliente?.Id.ToString() ?? "sin-match");
    }
}
