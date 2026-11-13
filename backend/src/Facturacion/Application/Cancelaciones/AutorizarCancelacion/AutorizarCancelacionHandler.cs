using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
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
/// <param name="Huella">M-235: huella de la solicitud que vio quien autoriza; si cambió, se bloquea.</param>
public sealed record AutorizarCancelacionCommand(Guid ComprobanteId, string? Huella = null) : IRequest<SolicitarCancelacionResponse>;

/// <summary>
/// H-U31: reenvía al PAC la MISMA solicitud que quedó pendiente porque el SAT aún no reflejaba el CFDI (205). No crea otra
/// solicitud ni otro folio; si el SAT ya lo refleja y acepta, el comprobante queda Cancelado como siempre.
/// </summary>
public sealed record ReintentarCancelacionCommand(Guid ComprobanteId) : IRequest<SolicitarCancelacionResponse>;

public sealed class AutorizarCancelacionHandler(
    FacturacionDbContext db, ICfdiTimbradoPort fiscal, IIntegrationEventPublisher eventos, ICurrentUserContext user, IClock clock,
    IOptions<CancelacionSatOpciones>? opcionesSat = null)
    : IRequestHandler<AutorizarCancelacionCommand, SolicitarCancelacionResponse>,
      IRequestHandler<ReintentarCancelacionCommand, SolicitarCancelacionResponse>
{
    public async Task<SolicitarCancelacionResponse> Handle(
        ReintentarCancelacionCommand command, CancellationToken cancellationToken)
    {
        if (user.UserId is null)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se identificó al usuario que reintenta.");

        var solicitud = await db.SolicitudesCancelacion
            .Where(s => s.ComprobanteId == command.ComprobanteId && s.Estado == Domain.Cancelaciones.EstadoSolicitudCancelacion.Solicitada)
            .OrderByDescending(s => s.SolicitadaEn)
            .FirstOrDefaultAsync(cancellationToken);
        if (solicitud is null || !solicitud.PendienteReintentoSat(CancelacionSat205.PrefijoAviso))
            throw new BusinessRuleException("CANCELACION_SIN_REINTENTO",
                "El comprobante no tiene una cancelación pendiente de reintento ante el SAT.");

        var comprobante = await CadenaCancelacion.CargarCancelableAsync(db, command.ComprobanteId, cancellationToken);
        solicitud.LimpiarAvisoReintento();
        await EnviarAlPacAsync(comprobante, solicitud, clock.UtcNow, esReintento: true, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return SolicitarCancelacionResponse.De(comprobante, solicitud);
    }

    public async Task<SolicitarCancelacionResponse> Handle(
        AutorizarCancelacionCommand command, CancellationToken cancellationToken)
    {
        if (user.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se identificó al usuario que autoriza.");

        var solicitud = await CadenaCancelacion.PendienteDeAsync(db, command.ComprobanteId, cancellationToken);
        var ahora = clock.UtcNow;

        // El comprobante pudo cambiar desde que se solicitó: se revisa otra vez antes de ir al PAC.
        var comprobante = await CadenaCancelacion.CargarCancelableAsync(db, command.ComprobanteId, cancellationToken);

        // M-235: si el importe cambió desde que se autorizó, la firma previa se anula y se bloquea la ejecución.
        if (solicitud.ActualizarImporte(Firmas.FirmasCancelacion.ImporteMxn(comprobante)))
        {
            await db.SaveChangesAsync(cancellationToken);
            throw new BusinessRuleException("CANCELACION_VERSION_CAMBIO",
                "El importe del comprobante cambió desde que se autorizó la cancelación. Revísala y vuelve a autorizar.");
        }

        // M-235: con dos firmas, la primera solo se registra; la segunda (otra persona) la envía al PAC.
        if (!solicitud.Autorizar(usuarioId, user.UserName, ahora, command.Huella))
        {
            await db.SaveChangesAsync(cancellationToken);
            return SolicitarCancelacionResponse.De(comprobante, solicitud);
        }

        await EnviarAlPacAsync(comprobante, solicitud, ahora, esReintento: false, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return SolicitarCancelacionResponse.De(comprobante, solicitud);
    }

    private async Task EnviarAlPacAsync(
        Comprobante comprobante, Domain.Cancelaciones.SolicitudCancelacion solicitud, DateTimeOffset ahora, bool esReintento,
        CancellationToken cancellationToken)
    {
        var resultado = await fiscal.CancelarAsync(
            new CancelacionSolicitud(comprobante.EmpresaId, comprobante.Uuid ?? string.Empty, comprobante.RfcEmisor, solicitud.MotivoSat, solicitud.UuidSustituto),
            cancellationToken);

        comprobante.MarcarCancelacionPendiente();

        switch (resultado.Estado)
        {
            case CancelacionEstado.Aceptada:
                comprobante.MarcarCancelado();
                solicitud.MarcarAceptada(resultado.EstatusSat, ahora);
                await AplicarEfectosCancelacionAsync(comprobante, solicitud, cancellationToken);
                // F10-PR1: evento de comprobante cancelado (reversa fiscal/financiera).
                await eventos.PublishAsync(EventosContablesFacturacion.ComprobanteCancelado(
                    comprobante, ahora, await PedidoDeAsync(comprobante, cancellationToken)),
                    cancellationToken);
                break;

            case CancelacionEstado.Solicitada:
            case CancelacionEstado.EnProceso:
                solicitud.MarcarEnProceso(resultado.EstatusSat);
                break;

            // H-U31: el SAT aún no refleja el CFDI (205): la factura sigue vigente y la misma solicitud queda pendiente de reintento.
            case CancelacionEstado.Rechazada or CancelacionEstado.Error when CancelacionSat205.EsNoReflejadoAun(resultado):
                comprobante.RevertirCancelacion();
                solicitud.MarcarPendienteReintentoSat(CancelacionSat205.Aviso(CancelacionSat205.ReintentoSugerido(
                    comprobante.FechaTimbrado, ahora, (opcionesSat?.Value ?? new CancelacionSatOpciones()).Intervalo, esReintento)));
                break;

            case CancelacionEstado.Rechazada:
            case CancelacionEstado.Error:
                comprobante.RevertirCancelacion();
                solicitud.MarcarRechazada(resultado.ErrorMensaje ?? resultado.EstatusSat, ahora);
                break;
        }
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
    private async Task AplicarEfectosCancelacionAsync(
        Comprobante comprobante, Domain.Cancelaciones.SolicitudCancelacion solicitud, CancellationToken cancellationToken)
    {
        // FAC-08b: cancelada por sustitución, el pedido y la salida pasan a la sustituta.
        if (await Sustituciones.SustitucionCfdi.TraspasarOrigenAsync(db, comprobante, solicitud, cancellationToken))
            return;

        switch (comprobante)
        {
            case FacturaVenta factura:
                // M-188: pedido vigente → re-facturable; cancelado en origen → la salida se descarta (sin PEDIDO_CANCELADO).
                await EfectosCancelacionPedido.AplicarAsync(db, factura, clock.UtcNow, cancellationToken);
                break;

            case FacturaAnticipo:
                var anticipo = await db.Anticipos.FirstOrDefaultAsync(a => a.FacturaAnticipoId == comprobante.Id, cancellationToken);
                anticipo?.Cancelar();
                break;
        }
    }
}
