using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.EventListeners;

/// <summary>
/// Handler MediatR del evento <c>cuentas_por_pagar.cfdi.ingresado.v1</c>.
/// Triggered desde el listener Service Bus.
///
/// <para>
/// <b>Lógica</b> (enlace diferido variante A, §5.4): busca recepciones
/// registradas con folio fiscal capturado a mano
/// (<c>cfdi_uuid_fiscal = UUID del evento</c>, <c>cfdi_recibido_id NULL</c>
/// — índice parcial <c>ix_movimientos_cfdi_uuid_pendiente</c>) y les
/// backfillea la referencia al <c>CfdiRecibido</c> recién ingresado.
/// Si no hay coincidencias, ignora silenciosamente (la gran mayoría de
/// los CFDIs ingresados no corresponden a recepciones pendientes).
/// </para>
/// <para>
/// <b>Idempotencia</b>: dedupe por <see cref="EventoProcesado"/> en el
/// listener envoltorio; además el predicado <c>cfdi_recibido_id NULL</c>
/// hace el enlace idempotente por sí mismo.
/// </para>
/// </summary>
public sealed record CfdiRecibidoIngresadoCommand(
    Guid EventId,
    CfdiRecibidoIngresadoPayload Payload) : IRequest;

public sealed class CfdiRecibidoIngresadoHandler
    : IRequestHandler<CfdiRecibidoIngresadoCommand>
{
    public const string EventType = "cuentas_por_pagar.cfdi.ingresado.v1";

    private readonly AlmacenDbContext _db;
    private readonly ILogger<CfdiRecibidoIngresadoHandler> _logger;

    public CfdiRecibidoIngresadoHandler(
        AlmacenDbContext db,
        ILogger<CfdiRecibidoIngresadoHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(
        CfdiRecibidoIngresadoCommand request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        // El publisher (VO UuidCfdi de CxP) ya normaliza a mayúsculas;
        // se re-normaliza por robustez ante versiones previas del payload.
        var uuid = payload.UuidCfdi.Trim().ToUpperInvariant();

        var recepcionesPendientes = await _db.Movimientos
            .Where(m => m.Tipo == TipoMovimiento.EntradaCompra
                && m.CfdiUuidFiscal == uuid
                && m.CfdiRecibidoId == null
                && m.Estado == EstadoMovimiento.Registrado)
            .ToListAsync(cancellationToken);

        if (recepcionesPendientes.Count == 0)
        {
            _logger.LogDebug(
                "CfdiRecibidoIngresado: UUID {Uuid} sin recepciones pendientes de enlace. Ignorando.",
                uuid);
            return;
        }

        foreach (var recepcion in recepcionesPendientes)
        {
            recepcion.EnlazarCfdiRecibido(payload.CfdiRecibidoId);
        }

        // Registra el evento procesado (idempotencia A12) en la misma TX.
        _db.Set<EventoProcesado>().Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            observaciones: $"cfdi={payload.CfdiRecibidoId}, uuid={uuid}, recepciones={recepcionesPendientes.Count}"));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "CfdiRecibidoIngresado procesado. Cfdi={CfdiId} Uuid={Uuid} RecepcionesEnlazadas={N}",
            payload.CfdiRecibidoId, uuid, recepcionesPendientes.Count);
    }
}
