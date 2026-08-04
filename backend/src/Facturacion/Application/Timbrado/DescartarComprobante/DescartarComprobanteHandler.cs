using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Timbrado.DescartarComprobante;

/// <summary>
/// Descarta un comprobante fallido ([Decisión 01-G] G3). Carga polimórfica
/// por la raíz TPT (mismo patrón que <c>ReintentarTimbradoHandler</c>); la
/// FSM valida <c>TimbradoFallido → Descartada</c>. Efectos por tipo — espejo
/// de los de la cancelación (<c>SolicitarCancelacionHandler</c>):
///
/// <list type="bullet">
/// <item><c>FacturaVenta</c>: si esta factura tenía tomado su pedido (G5),
/// lo libera (<c>RevertirAFacturable</c>) y un pedido A+W avisa de vuelta a
/// la tabla-puente con <c>SinFacturar</c> (nunca hubo UUID).</item>
/// <item><c>FacturaAnticipo</c>: cancela el agregado <c>Anticipo</c> (se creó
/// en la emisión aunque el timbre fallara; sin CFDI vigente no debe quedar
/// amortizable).</item>
/// <item>NC / REPP / Carta Porte: sin efectos adicionales (una fallida no
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
            case FacturaVenta { PedidoFacturableId: { } pedidoId } factura:
                var pedido = await _db.PedidosFacturables
                    .FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken);

                // Solo se libera si ESTA factura lo tenía tomado (G5). Un
                // pedido cancelado en origen o re-facturado por otra vía
                // tiene ComprobanteVigenteId distinto (o null) y no se toca.
                if (pedido?.ComprobanteVigenteId == factura.Id)
                {
                    pedido.RevertirAFacturable();
                    pedidoLiberadoId = pedido.Id;

                    if (pedido.Origen == OrigenPedido.Aw)
                    {
                        var control = await _db.IngestaControles
                            .FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, cancellationToken);
                        if (control is not null)
                        {
                            control.SolicitarWriteBackEstado("SinFacturar", uuid: null, _clock.UtcNow);
                            control.CambiarEstado(EstadoIngesta.Importado);
                        }
                    }
                }
                break;

            case Millet.Facturacion.Domain.Anticipos.FacturaAnticipo:
                var anticipo = await _db.Anticipos
                    .FirstOrDefaultAsync(a => a.FacturaAnticipoId == comprobante.Id, cancellationToken);
                anticipo?.Cancelar();
                break;
        }

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
