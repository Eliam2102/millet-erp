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

    // ───────────── M-285 (86e3zr7af): efectos registrados de la factura de venta ─────────────

    /// <summary>NC de ranura emitida por el último <see cref="EjecutarPendientesAsync"/> (la emisión la devuelve).</summary>
    public NotaCreditoRanuraEmitida? UltimaNcRanura { get; private set; }

    /// <summary>
    /// Registra como «Pendiente» los efectos que aplican a la factura de venta timbrada. Se guardan en el
    /// mismo <c>SaveChanges</c> que el timbrado; no se ejecutan aquí.
    /// </summary>
    public async Task RegistrarAsync(FacturaVenta factura, Guid? clienteId, CancellationToken ct, bool incluirSustitucion = true)
    {
        var tipos = new List<TipoEfectoComprobante> { TipoEfectoComprobante.EventoContable };
        if (factura.PedidoFacturableId is not null)
            tipos.AddRange([TipoEfectoComprobante.EstadoPedido, TipoEfectoComprobante.NcRanura]);
        if (incluirSustitucion && factura.Relaciones.Any(r => r.TipoRelacion == Sustituciones.SustitucionCfdi.TipoRelacion))
            tipos.Add(TipoEfectoComprobante.CancelacionSustituida);

        var existentes = await _db.EfectosComprobante.Where(e => e.ComprobanteId == factura.Id)
            .Select(e => e.Tipo).ToListAsync(ct);
        foreach (var tipo in tipos.Except(existentes))
            _db.EfectosComprobante.Add(EfectoComprobante.Pendiente(factura.EmpresaId, factura.Id, tipo,
                tipo == TipoEfectoComprobante.EventoContable ? clienteId : null));
    }

    /// <summary>
    /// Aplica los efectos pendientes de la factura, cada uno con su propio guardado. Si uno falla, sus
    /// cambios se descartan y el error queda en el efecto; el timbrado y los demás efectos se conservan.
    /// Debe llamarse con el contexto ya guardado (sin cambios pendientes). Devuelve los que siguen pendientes.
    /// </summary>
    public async Task<IReadOnlyList<EfectoComprobante>> EjecutarPendientesAsync(
        FacturaVenta factura, DateTimeOffset ahora, CancellationToken ct, Func<TipoEfectoComprobante, bool>? filtro = null)
    {
        var pendientes = (await _db.EfectosComprobante
            .Where(e => e.ComprobanteId == factura.Id && e.Estado == EstadoEfectoComprobante.Pendiente)
            .OrderBy(e => e.Tipo).ToListAsync(ct))
            .Where(e => filtro?.Invoke(e.Tipo) ?? true).ToList();
        foreach (var efecto in pendientes)
        {
            try
            {
                await EjecutarAsync(factura, efecto, ahora, ct);
                efecto.MarcarAplicado(ahora);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DescartarCambios();
                // Otro reintento concurrente lo aplicó (su guardado ganó): no se le registra error.
                if (efecto.Estado == EstadoEfectoComprobante.Aplicado) continue;
                efecto.RegistrarError(ex is Millet.SharedKernel.Domain.Exceptions.DomainException d ? d.Code : ex.GetType().Name,
                    ex.Message, ahora);
                await _db.SaveChangesAsync(ct);
            }
        }
        return pendientes.Where(e => e.Estado == EstadoEfectoComprobante.Pendiente).ToList();
    }

    private async Task EjecutarAsync(FacturaVenta factura, EfectoComprobante efecto, DateTimeOffset ahora, CancellationToken ct)
    {
        switch (efecto.Tipo)
        {
            case TipoEfectoComprobante.EventoContable:
                var pedidoEvt = factura.PedidoFacturableId is Guid pid
                    ? await _db.PedidosFacturables.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct)
                    : null;
                await _eventos.PublishAsync(EventosContablesFacturacion.FacturaVentaTimbrada(
                    factura, ahora, efecto.ClienteId ?? await ClienteContableFacturacion.ResolverAsync(_db, factura, ct),
                    await EventosContablesFacturacion.TiposProductoAsync(_productos, factura, ct), pedidoEvt), ct);
                break;

            case TipoEfectoComprobante.EstadoPedido:
                await EstadoPedidoAsync(factura, ct);
                break;

            case TipoEfectoComprobante.NcRanura:
                var pedido = factura.PedidoFacturableId is Guid id
                    ? await _db.PedidosFacturables.FirstOrDefaultAsync(p => p.Id == id, ct)
                    : null;
                UltimaNcRanura = await NcRanuraEmisor.EmitirSiAplicaAsync(
                    _db, _sender, _fiscal, _cfdiRepo, _eventos,
                    factura, pedido, usuarioEmisorId: factura.UsuarioEmisorId, ahora, _receptorFiscal, ct);
                break;

            case TipoEfectoComprobante.CancelacionSustituida:
                await Sustituciones.SustitucionCfdi.SolicitarCancelacionOriginalAsync(
                    _db, factura, factura.UsuarioEmisorId, null, ahora, ct);
                break;
        }
    }

    /// <summary>
    /// Toma del pedido o de la salida (G5, FAC-08, M8) y control de ingesta de A+W en Facturado.
    /// Idempotente: un pedido ya Facturado no cambia.
    /// </summary>
    private async Task EstadoPedidoAsync(FacturaVenta factura, CancellationToken ct)
    {
        if (factura.PedidoFacturableId is not Guid pedidoId) return;
        var pedido = await _db.PedidosFacturables.FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null) return;
        if (factura.SalidaFacturableId is not null)
            await Pedidos.PedidoPorParcialidades.TomarPedidoSiCompletoAsync(_db, factura, pedido, ct);
        else if (pedido.Estado == EstadoPedidoFacturable.Importado)
            pedido.MarcarFacturado(factura.Id);

        if (pedido.ComprobanteVigenteId == factura.Id && pedido.Origen == OrigenPedido.Aw
            && factura.Estado == EstadoTimbrado.Timbrado)
        {
            var control = await _db.IngestaControles.FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, ct);
            control?.CambiarEstado(EstadoIngesta.Facturado);
        }
    }

    /// <summary>Deshace en el contexto lo que dejó a medias un efecto fallido (lo guardado no cambia).</summary>
    private void DescartarCambios()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.Reload();
                    break;
            }
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
