using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.SolicitarReembolso;
using Millet.Facturacion.Application.NotasCredito;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using DominioCartaPorte = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.Application.Timbrado;

/// <summary>
/// Efectos posteriores al timbrado de cada tipo de comprobante (M-284, 86e3zr751): sustitución,
/// evento contable, toma del pedido o salida, control de ingesta A+W, NC de ranura, amortización y
/// reembolso de NC, pendiente de REP. Se extrajeron del reintento de timbrado sin cambiar su
/// comportamiento; los usan el reintento y la adopción de un CFDI que sí timbró.
/// </summary>
public sealed class EfectosPostTimbrado(
    FacturacionDbContext db, ISender sender, ICfdiTimbradoPort fiscal, ICfdiRepositorioPort cfdiRepo,
    IIntegrationEventPublisher eventos, ValidadorReceptorFiscal receptorFiscal, IProductosReadPort? productos)
{
    private readonly FacturacionDbContext _db = db;
    private readonly ISender _sender = sender;
    private readonly ICfdiTimbradoPort _fiscal = fiscal;
    private readonly ICfdiRepositorioPort _cfdiRepo = cfdiRepo;
    private readonly IIntegrationEventPublisher _eventos = eventos;
    private readonly ValidadorReceptorFiscal _receptorFiscal = receptorFiscal;
    private readonly IProductosReadPort? _productos = productos;

    /// <summary>Despacha por tipo. La Carta Porte no tiene efectos posteriores.</summary>
    public Task AplicarAsync(Comprobante comprobante, DateTimeOffset ahora, CancellationToken ct) => comprobante switch
    {
        FacturaVenta fv => FacturaVentaAsync(fv, ahora, ct),
        FacturaAnticipo fa => FacturaAnticipoAsync(fa, ahora, ct),
        NotaCredito nc => NotaCreditoAsync(nc, ahora, ct),
        ReciboPago repp => ReciboPagoAsync(repp, ahora, ct),
        _ => Task.CompletedTask,
    };

    /// <summary>Efectos de un/a FacturaVenta timbrado/a (los mismos de su emisión).</summary>
    public async Task FacturaVentaAsync(FacturaVenta factura, DateTimeOffset ahora, CancellationToken ct)
    {
        // FAC-08b: si es sustituta, al quedar timbrada se solicita la cancelación de la original.
        await Sustituciones.SustitucionCfdi.SolicitarCancelacionOriginalAsync(
            _db, factura, factura.UsuarioEmisorId, null, ahora, ct);

        // Efectos de EmitirFacturaVentaHandler pasos 9 y B2/B3 (evento +
        // asiento + pedido + write-back A+W) que el intento fallido omitió.
        if (factura.Estado == EstadoTimbrado.Timbrado)
        {
            await _eventos.PublishAsync(EventosContablesFacturacion.FacturaVentaTimbrada(
                factura, ahora, clienteId: await ClienteContableFacturacion.ResolverAsync(_db, factura, ct),
                tiposProducto: await EventosContablesFacturacion.TiposProductoAsync(_productos, factura, ct),
                pedido: factura.PedidoFacturableId is Guid pedidoEvt
                    ? await _db.PedidosFacturables.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pedidoEvt, ct)
                    : null),
                ct);
        }

        if (factura.PedidoFacturableId is Guid pedidoId
            && factura.Estado != EstadoTimbrado.TimbradoFallido)
        {
            var pedido = await _db.PedidosFacturables
                .FirstOrDefaultAsync(p => p.Id == pedidoId, ct);

            // [Decisión 01-G] G5: desde F13 la emisión toma el pedido en el
            // primer intento, así que aquí normalmente ya está Facturado
            // apuntando a ESTA factura. El branch Importado cubre fallidas
            // previas a G5 (el pedido quedaba libre al fallar el timbre).
            // FAC-08: la factura de una salida no toma el pedido; M8: solo la que completa el 100 %.
            if (factura.SalidaFacturableId is not null)
            {
                if (pedido is not null)
                    await Pedidos.PedidoPorParcialidades.TomarPedidoSiCompletoAsync(_db, factura, pedido, ct);
            }
            else if (pedido is { Estado: EstadoPedidoFacturable.Importado })
                pedido.MarcarFacturado(factura.Id);

            if (pedido?.ComprobanteVigenteId == factura.Id
                && pedido.Origen == OrigenPedido.Aw
                && factura.Estado == EstadoTimbrado.Timbrado)
            {
                var control = await _db.IngestaControles
                    .FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, ct);
                if (control is not null)
                {
                    control.CambiarEstado(EstadoIngesta.Facturado);
                }
            }

            // RANURA-PR2: la emisión original no alcanzó a emitir la NC de la
            // ranura (el timbre falló antes) — se emite aquí al quedar
            // Timbrada. Idempotente si ya existe una NC de ranura vigente.
            await NcRanuraEmisor.EmitirSiAplicaAsync(
                _db, _sender, _fiscal, _cfdiRepo, _eventos,
                factura, pedido, usuarioEmisorId: null, ahora, _receptorFiscal, ct);
        }
    }

    /// <summary>Efectos de un/a FacturaAnticipo timbrado/a (los mismos de su emisión).</summary>
    public async Task FacturaAnticipoAsync(FacturaAnticipo factura, DateTimeOffset ahora, CancellationToken ct)
    {
        if (factura.Estado == EstadoTimbrado.Timbrado)
        {
            // El agregado Anticipo se creó en la emisión original aunque el
            // timbre fallara (EmitirFacturaAnticipoHandler persiste ambos).
            var anticipo = await _db.Anticipos
                .FirstOrDefaultAsync(a => a.FacturaAnticipoId == factura.Id, ct)
                ?? throw new EntityNotFoundException(
                    "ANTICIPO_NO_ENCONTRADO",
                    $"No existe el anticipo de la factura de anticipo {factura.Id}.");

            await _eventos.PublishAsync(EventosContablesFacturacion.FacturaAnticipoTimbrada(factura, ahora, anticipo.ClienteId),
                ct);
        }
    }

    /// <summary>Efectos de un/a NotaCredito timbrado/a (los mismos de su emisión).</summary>
    public async Task NotaCreditoAsync(NotaCredito nc, DateTimeOffset ahora, CancellationToken ct)
    {
        if (nc.Estado == EstadoTimbrado.Timbrado)
        {
            // U-1 (86e3yxn2p): la NC de amortización que quedó pendiente al emitir la
            // factura dejó el importe comprometido; timbrada, ya amortiza el anticipo.
            if (nc.Motivo == MotivoNotaCredito.Amortizacion
                && nc.AnticipoOrigenId is Guid anticipoId && nc.FacturaRelacionadaId is Guid facturaId)
            {
                var anticipo = await _db.Anticipos.Include(a => a.Vinculaciones)
                    .FirstOrDefaultAsync(a => a.Id == anticipoId, ct);
                if (anticipo is not null && anticipo.Vinculaciones.Any(v => v.FacturaVentaId == facturaId && v.NcAmortizacionId is null))
                    anticipo.RegistrarAmortizacion(facturaId, nc.Id, nc.Total, ahora);
            }
            await _eventos.PublishAsync(EventosContablesFacturacion.NotaCreditoTimbrada(
                nc, ahora, clienteId: await ClienteContableFacturacion.ResolverAsync(_db, nc, ct)), ct);
            // F6.3: el reembolso programado al emitir sale cuando por fin queda timbrada.
            // Sin usuario en el reintento: queda como solicitante quien emitió la nota.
            await ReembolsoNotaCredito.SolicitarProgramadoSiAplicaAsync(_db, _eventos, nc, usuarioId: null, ahora, ct);
        }
    }

    /// <summary>Efectos de un/a ReciboPago timbrado/a (los mismos de su emisión).</summary>
    public async Task ReciboPagoAsync(ReciboPago repp, DateTimeOffset ahora, CancellationToken ct)
    {
        var pendiente = await _db.ReppPendientes.SingleOrDefaultAsync(p => p.IntentoReciboPagoId == repp.Id, ct);
        if (repp.Estado == EstadoTimbrado.Timbrado)
        {
            await _eventos.PublishAsync(new ReciboPagoTimbradoIntegrationEvent(
                repp.EmpresaId, ahora, repp.Id, repp.Uuid!, repp.ImporteTotalPago,
                repp.FacturasPagadas.Sum(f => f.GananciaPerdidaCambiaria),
                repp.FacturasPagadas.Select(f => new ReppFacturaPagadaDetalle(
                    f.FacturaVentaId, f.ImportePagado, f.NumParcialidad, f.MonedaFactura, f.SaldoInsoluto)).ToList(), pendiente?.MovimientoBancarioId, pendiente?.PropuestaId), ct);
        }

        pendiente?.RegistrarIntento(repp);
    }
}
