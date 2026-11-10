using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Timbrado.DescartarComprobante;

/// <summary>
/// Descarta un comprobante fallido ([Decisión 01-G] G3). Carga polimórfica
/// por la raíz TPT (mismo patrón que <c>ReintentarTimbradoHandler</c>); la
/// FSM valida <c>TimbradoFallido → Descartada</c>. Efectos por tipo — espejo
/// de los de la cancelación (<c>AutorizarCancelacionHandler</c>):
///
/// <list type="bullet">
/// <item><c>FacturaVenta</c>: si esta factura tenía tomado su pedido (G5),
/// lo libera (<c>RevertirAFacturable</c>) y un pedido A+W avisa de vuelta a
/// la tabla-puente con <c>SinFacturar</c> (nunca hubo UUID).</item>
/// <item><c>FacturaAnticipo</c>: cancela el agregado <c>Anticipo</c> (se creó
/// en la emisión aunque el timbre fallara; sin CFDI vigente no debe quedar
/// amortizable).</item>
/// <item>NC de amortización pendiente (U-1): libera el importe comprometido
/// con su factura en el anticipo.</item>
/// <item>Otras NC / REPP / Carta Porte: sin efectos adicionales (una fallida no
/// aplicó saldos ni publicó eventos).</item>
/// </list>
/// </summary>
public sealed class DescartarComprobanteHandler
    : IRequestHandler<DescartarComprobanteCommand, DescartarComprobanteResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IClock _clock;

    public DescartarComprobanteHandler(FacturacionDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<DescartarComprobanteResponse> Handle(
        DescartarComprobanteCommand command, CancellationToken cancellationToken)
    {
        var comprobante = await _db.Comprobantes
            .FirstOrDefaultAsync(c => c.Id == command.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante {command.ComprobanteId}.");

        comprobante.Descartar();

        Guid? pedidoLiberadoId = null;
        switch (comprobante)
        {
            case FacturaVenta factura:
                // Solo se libera si ESTA factura lo tenía tomado (G5); un pedido re-facturado por otra vía
                // no se toca. M-188: con el pedido cancelado en origen, la salida de la factura se descarta.
                pedidoLiberadoId = (await Millet.Facturacion.Application.Cancelaciones.EfectosCancelacionPedido.AplicarAsync(
                    _db, factura, _clock.UtcNow, cancellationToken, soloSiEsLaVigente: true))?.Id;
                break;

            case Millet.Facturacion.Domain.Anticipos.FacturaAnticipo:
                var anticipo = await _db.Anticipos
                    .FirstOrDefaultAsync(a => a.FacturaAnticipoId == comprobante.Id, cancellationToken);
                anticipo?.Cancelar();
                break;

            // U-1 (86e3yxn2p): descartar la NC de amortización pendiente libera el
            // importe que quedó comprometido con su factura.
            case Millet.Facturacion.Domain.NotasCredito.NotaCredito
            {
                Motivo: Millet.Facturacion.Domain.NotasCredito.MotivoNotaCredito.Amortizacion,
                AnticipoOrigenId: { } anticipoOrigenId, FacturaRelacionadaId: { } facturaRelacionadaId,
            }:
                var anticipoNc = await _db.Anticipos.Include(a => a.Vinculaciones)
                    .FirstOrDefaultAsync(a => a.Id == anticipoOrigenId, cancellationToken);
                anticipoNc?.LiberarVinculacion(facturaRelacionadaId);
                break;
        }

        var pendiente = await _db.ReppPendientes.SingleOrDefaultAsync(p => p.IntentoReciboPagoId == comprobante.Id, cancellationToken);
        pendiente?.LiberarIntentoDescartado();
        // F6.8: la contingencia del documento queda descartada (folio quemado).
        await Contingencias.ContingenciasTimbrado.AplicarDescarteAsync(
            _db, comprobante, _clock.UtcNow, null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return new DescartarComprobanteResponse(
            Id: comprobante.Id,
            Tipo: comprobante.GetType().Name,
            Estado: comprobante.Estado.ToString(),
            Folio: comprobante.Folio,
            PedidoLiberadoId: pedidoLiberadoId,
            Version: comprobante.Version);
    }
}
