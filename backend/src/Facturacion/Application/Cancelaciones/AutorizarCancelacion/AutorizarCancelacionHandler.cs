using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cancelaciones.AutorizarCancelacion;

/// <summary>
/// El Contador General autoriza una cancelación pendiente (F6.5) y solo entonces se
/// envía al PAC con el flujo de siempre: si el PAC acepta, cancela y revierte efectos;
/// si queda en proceso, el <c>CancelacionSatPollerWorker</c> lo resuelve; si lo
/// rechaza, el comprobante vuelve a Timbrado. Quien solicitó no puede autorizar.
/// </summary>
public sealed record AutorizarCancelacionCommand(Guid ComprobanteId) : IRequest<SolicitarCancelacionResponse>;

public sealed class AutorizarCancelacionHandler(
    FacturacionDbContext db, ICfdiTimbradoPort fiscal, IIntegrationEventPublisher eventos, ICurrentUserContext user, IClock clock)
    : IRequestHandler<AutorizarCancelacionCommand, SolicitarCancelacionResponse>
{
    public async Task<SolicitarCancelacionResponse> Handle(
        AutorizarCancelacionCommand command, CancellationToken cancellationToken)
    {
        if (user.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se identificó al usuario que autoriza.");

        var solicitud = await CadenaCancelacion.PendienteDeAsync(db, command.ComprobanteId, cancellationToken);
        var ahora = clock.UtcNow;
        solicitud.Autorizar(usuarioId, user.UserName, ahora);

        // El comprobante pudo cambiar desde que se solicitó: se revisa otra vez antes de ir al PAC.
        var comprobante = await CadenaCancelacion.CargarCancelableAsync(db, command.ComprobanteId, cancellationToken);

        var resultado = await fiscal.CancelarAsync(
            new CancelacionSolicitud(comprobante.EmpresaId, comprobante.Uuid ?? string.Empty, comprobante.RfcEmisor, solicitud.MotivoSat, solicitud.UuidSustituto),
            cancellationToken);

        comprobante.MarcarCancelacionPendiente();

        switch (resultado.Estado)
        {
            case CancelacionEstado.Aceptada:
                comprobante.MarcarCancelado();
                solicitud.MarcarAceptada(resultado.EstatusSat, ahora);
                await AplicarEfectosCancelacionAsync(comprobante, cancellationToken);
                // F10-PR1: evento de comprobante cancelado (reversa fiscal/financiera).
                await eventos.PublishAsync(EventosContablesFacturacion.ComprobanteCancelado(
                    comprobante, ahora, await PedidoDeAsync(comprobante, cancellationToken)),
                    cancellationToken);
                break;

            case CancelacionEstado.Solicitada:
            case CancelacionEstado.EnProceso:
                solicitud.MarcarEnProceso(resultado.EstatusSat);
                break;

            case CancelacionEstado.Rechazada:
            case CancelacionEstado.Error:
                comprobante.RevertirCancelacion();
                solicitud.MarcarRechazada(resultado.ErrorMensaje ?? resultado.EstatusSat, ahora);
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return SolicitarCancelacionResponse.De(comprobante, solicitud);
    }

    private async Task<PedidoFacturable?> PedidoDeAsync(Comprobante c, CancellationToken ct) =>
        c is FacturaVenta { PedidoFacturableId: { } id }
            ? await db.PedidosFacturables.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            : null;

    /// <summary>
    /// Efectos de una cancelación aceptada: la factura final libera su pedido
    /// (vuelve a Importado, re-facturable, invariante 5); el anticipo cancelado
    /// pasa a Cancelado.
    /// </summary>
    private async Task AplicarEfectosCancelacionAsync(Comprobante comprobante, CancellationToken cancellationToken)
    {
        switch (comprobante)
        {
            case FacturaVenta { PedidoFacturableId: { } pedidoId }:
                var pedido = await db.PedidosFacturables.FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken);
                pedido?.RevertirAFacturable();

                // TR-01: sin write-back por pedido; la cancelación hacia A+W es otro tipo de fila (fuera de TR-01).
                if (pedido?.Origen == OrigenPedido.Aw)
                {
                    var control = await db.IngestaControles
                        .FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, cancellationToken);
                    if (control is not null)
                    {
                        control.CambiarEstado(EstadoIngesta.Importado);
                    }
                }
                break;

            case FacturaAnticipo:
                var anticipo = await db.Anticipos.FirstOrDefaultAsync(a => a.FacturaAnticipoId == comprobante.Id, cancellationToken);
                anticipo?.Cancelar();
                break;
        }
    }
}
