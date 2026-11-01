using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Domain.Devoluciones;

namespace Millet.Tesoreria.Application.Integration;

/// <summary>
/// <c>tesoreria.reembolso-nota-credito.actualizado.v1</c> (F6.3) — cambió el estado de la devolución de dinero que nació de
/// una NC de cliente: autorizada, rechazada, cancelada o pagada. Facturación lo muestra en la NC; en «Pagada» van la fecha
/// valor y la referencia bancaria. Solo para devoluciones de origen NC (las de depósito erróneo no tienen NC).
/// Espejo: Facturacion/Application/EventListeners/ContratosEspejoTesoreria.cs.
/// </summary>
public sealed record ReembolsoNotaCreditoActualizadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid DevolucionId,
    string DevolucionFolio,
    Guid NotaCreditoId,
    string Estado,
    decimal Importe,
    string Moneda,
    DateOnly? FechaPago,
    string? ReferenciaBancaria,
    string? Motivo)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "tesoreria.reembolso-nota-credito.actualizado.v1";
}

/// <summary>Publica el cambio de estado de una devolución de origen NC; las de depósito erróneo no publican nada.</summary>
public static class ReembolsoNotaCreditoEventos
{
    public static async Task PublicarSiEsDeNotaCreditoAsync(
        IIntegrationEventPublisher publisher, DevolucionCliente d, DateTimeOffset ahora, CancellationToken ct,
        DateOnly? fechaPago = null, string? referencia = null)
    {
        if (d.Origen != OrigenDevolucionCliente.NotaCredito || d.NotaCreditoId is not Guid notaCreditoId) return;
        var motivo = d.Estado switch
        {
            EstadoDevolucionCliente.Rechazada => d.MotivoRechazo,
            EstadoDevolucionCliente.Cancelada => d.MotivoCancelacion,
            _ => null,
        };
        await publisher.PublishAsync(new ReembolsoNotaCreditoActualizadoIntegrationEvent(
            d.EmpresaId, ahora, d.Id, d.Folio, notaCreditoId, d.Estado.ToString(), d.Importe, d.Moneda,
            fechaPago, referencia, motivo), ct);
    }
}
