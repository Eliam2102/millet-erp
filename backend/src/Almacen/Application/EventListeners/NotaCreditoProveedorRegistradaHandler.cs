using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.EventListeners;

/// <summary>
/// Espejo del payload de CxP <c>cuentas_por_pagar.nota-credito.registrada.v1</c>.
/// Almacén suscribe pero solo procesa los eventos con
/// <c>TipoRelacionCfdi=3</c> (devolución, según c_TipoRelacion del SAT).
/// </summary>
public sealed record NotaCreditoProveedorRegistradaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCreditoId,
    Guid ProveedorId,
    Guid? FacturaOrigenId,
    int TipoRelacionCfdi,
    decimal Total);

/// <summary>
/// Handler MediatR del evento de CxP. <b>Solo informativo</b>: ya no concilia
/// devoluciones (antes adivinaba la más antigua por proveedor+factura). La conciliación
/// exacta la hace <see cref="NcFiscalDevolucionRecibidaHandler"/>.
/// </summary>
public sealed record NotaCreditoProveedorRegistradaCommand(
    Guid EventId,
    NotaCreditoProveedorRegistradaPayload Payload) : IRequest;

public sealed class NotaCreditoProveedorRegistradaHandler
    : IRequestHandler<NotaCreditoProveedorRegistradaCommand>
{
    public const string EventType = "cuentas_por_pagar.nota-credito.registrada.v1";

    /// <summary>
    /// Tipo de relación CFDI 03 = "Devolución de la mercancía o nota
    /// de crédito de los documentos relacionados" (catálogo SAT).
    /// </summary>
    public const int TipoCfdiDevolucion = 3;

    private readonly AlmacenDbContext _db;
    private readonly ILogger<NotaCreditoProveedorRegistradaHandler> _logger;

    public NotaCreditoProveedorRegistradaHandler(
        AlmacenDbContext db,
        ILogger<NotaCreditoProveedorRegistradaHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(
        NotaCreditoProveedorRegistradaCommand request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;

        if (payload.TipoRelacionCfdi != TipoCfdiDevolucion)
        {
            _logger.LogDebug(
                "NotaCreditoProveedorRegistrada con TipoRelacionCfdi={Tipo}; Almacén solo procesa tipo {Devolucion}. Ignorando.",
                payload.TipoRelacionCfdi, TipoCfdiDevolucion);
            // Aún registramos el evento como procesado para que
            // el listener no lo re-ofrezca al worker.
            _db.Set<EventoProcesado>().Add(new EventoProcesado(
                request.EventId, EventType,
                $"TipoRelacionCfdi={payload.TipoRelacionCfdi} (no devolución, ignorado)"));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Solo informativo: la conciliación y el acreditado de la OC ocurren al formalizar la NotaCargo
        // (cuentas_por_pagar.nc-fiscal-devolucion.recibida.v1, con el Id exacto) en NcFiscalDevolucionRecibidaHandler.
        _logger.LogInformation(
            "NotaCreditoProveedorRegistrada (devolución) NC={Nc} proveedor={Proveedor} factura={Factura}: informativo; no concilia.",
            payload.NotaCreditoId, payload.ProveedorId, payload.FacturaOrigenId);
        _db.Set<EventoProcesado>().Add(new EventoProcesado(
            request.EventId, EventType, $"NC={payload.NotaCreditoId} (informativo; concilia nc-fiscal-devolucion.recibida)"));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
