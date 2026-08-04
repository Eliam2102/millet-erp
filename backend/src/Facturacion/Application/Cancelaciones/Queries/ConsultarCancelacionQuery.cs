using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cancelaciones.Queries;

/// <summary>
/// Estatus de cancelación de un comprobante (§7.2, GET /comprobantes/{id}/cancelar):
/// el estado del comprobante + la última solicitud de cancelación (si existe).
/// </summary>
public sealed record ConsultarCancelacionQuery(Guid ComprobanteId) : IRequest<ConsultarCancelacionResponse>;

public sealed record ConsultarCancelacionResponse(
    Guid ComprobanteId,
    string EstadoComprobante,
    Guid? SolicitudId,
    string? EstadoSolicitud,
    string? MotivoSat,
    string? EstatusSat,
    string? MensajeError,
    DateTimeOffset? SolicitadaEn,
    DateTimeOffset? ResueltaEn);

public sealed class ConsultarCancelacionHandler
    : IRequestHandler<ConsultarCancelacionQuery, ConsultarCancelacionResponse>
{
    private readonly FacturacionDbContext _db;

    public ConsultarCancelacionHandler(FacturacionDbContext db) => _db = db;

    public async Task<ConsultarCancelacionResponse> Handle(
        ConsultarCancelacionQuery query, CancellationToken cancellationToken)
    {
        var comprobante = await _db.Comprobantes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException("COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante {query.ComprobanteId}.");

        var solicitud = await _db.SolicitudesCancelacion.AsNoTracking()
            .Where(s => s.ComprobanteId == query.ComprobanteId)
            .OrderByDescending(s => s.SolicitadaEn)
            .FirstOrDefaultAsync(cancellationToken);

        return new ConsultarCancelacionResponse(
            ComprobanteId: comprobante.Id,
            EstadoComprobante: comprobante.Estado.ToString(),
            SolicitudId: solicitud?.Id,
            EstadoSolicitud: solicitud?.Estado.ToString(),
            MotivoSat: solicitud?.MotivoSat,
            EstatusSat: solicitud?.EstatusSat,
            MensajeError: solicitud?.MensajeError,
            SolicitadaEn: solicitud?.SolicitadaEn,
            ResueltaEn: solicitud?.ResueltaEn);
    }
}
