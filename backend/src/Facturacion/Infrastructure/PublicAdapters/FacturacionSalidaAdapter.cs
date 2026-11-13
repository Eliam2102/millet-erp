using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;
using Millet.Facturacion.Application.Pedidos.CancelarSalidaFacturable;
using Millet.Facturacion.Application.Pedidos.RegistrarSalidaFacturable;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Pedidos;
using Millet.ProductoTerminado.Application.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// PT5.5: la hoja de salida emitida por Producto Terminado es la salida real que Facturación factura (FAC-08), y su Carta Porte
/// sale con la funcionalidad existente. Reemplaza la simulación: ya no se registran a mano ni se esperan del stub.
/// </summary>
public sealed class FacturacionSalidaAdapter(ISender sender, IEmpresaFiscalReadPort empresas, ICurrentEmpresaContext empresa, FacturacionDbContext db) : IFacturacionSalidaPort
{
    // ponytail: clave SAT provisional del vidrio hasta tener una Carta Porte real de Logística (D15).
    private const string BienesTranspVidrio = "30171500";

    public async Task RegistrarSalidaAsync(Guid pedidoFacturableId, string referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> lineas, CancellationToken ct) =>
        await sender.Send(new RegistrarSalidaFacturableCommand(pedidoFacturableId, null, OrigenSalidaFacturable.ProductoTerminado, referencia,
            lineas.Select(l => new SalidaFacturableLineaDto(l.Posicion, l.Cantidad)).ToList()), ct);

    public async Task<CancelacionSalidaPt> CancelarSalidaAsync(string referencia, string motivo, CancellationToken ct)
    {
        var r = await sender.Send(new CancelarSalidaProductoTerminadoCommand(referencia, motivo), ct);
        return new CancelacionSalidaPt(r.Registrada, r.Descartada, r.EnRevision);
    }

    public async Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte solicitud, CancellationToken ct)
    {
        var empresaId = empresa.Current ?? throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");
        var emisor = await EmisorSnapshot.ResolverAsync(empresas, empresaId, ct);
        // Traslado (T): mercancía propia, el receptor es la misma empresa y no se factura servicio.
        var r = await sender.Send(new EmitirCartaPorteCommand(solicitud.SucursalId, null, "T", emisor.Rfc, emisor.Nombre, emisor.RegimenFiscal, emisor.LugarExpedicion, "S01", "MEX",
            emisor.Rfc, emisor.RegimenFiscal, "MXN", solicitud.Origen, solicitud.Destino, solicitud.DistanciaKm, solicitud.VehiculoId, solicitud.OperadorId, solicitud.PedidoFacturableId, solicitud.FechaSalida, solicitud.FechaSalida.AddHours(8),
            0m, null, solicitud.Mercancias.Select(m => new CartaPorteMercanciaInput(m.Descripcion, BienesTranspVidrio, "KGM", solicitud.PesoKg, solicitud.PesoKg, false)).ToList(),
            solicitud.OrigenCodigoPostal, solicitud.OrigenEstado, solicitud.DestinoCodigoPostal, solicitud.DestinoEstado), ct);
        return new CartaPorteSalida(r.Id, r.Folio);
    }

    public async Task<IReadOnlyDictionary<string, FacturaAlbaranRuta>> FacturasDeAlbaranesAsync(IReadOnlyList<string> albaranFolios, CancellationToken ct) =>
        (await sender.Send(new FacturasAlbaranesPtQuery(albaranFolios), ct))
            .ToDictionary(f => f.AlbaranFolio, f => new FacturaAlbaranRuta(f.AlbaranFolio, f.ComprobanteId, f.Folio, f.PorCobrar, f.CobroVigenteId));

    public async Task<IReadOnlyList<CobroRutaRegistrado>> RegistrarCobrosRutaAsync(Guid sucursalId, IReadOnlyList<CobroRutaAlbaran> cobros, CancellationToken ct) =>
        [.. (await sender.Send(new RegistrarCobrosRutaPtCommand(sucursalId, [.. cobros.Select(c =>
                new CobroRutaAlbaranPtInput(c.AlbaranFolio, c.Efectivo, c.Voucher, c.VoucherReferencia, c.TipoTarjeta, c.TarjetaUltimos4))]), ct))
            .Select(r => new CobroRutaRegistrado(r.AlbaranFolio, r.CobroId))];

    public async Task<IReadOnlyList<FacturaPedidoSalida>> FacturasDePedidosAsync(IReadOnlyList<Guid> pedidoFacturableIds, CancellationToken ct) =>
        await (from f in db.FacturasVenta.AsNoTracking()
               join p in db.PedidosFacturables.AsNoTracking() on f.PedidoFacturableId equals (Guid?)p.Id
               where pedidoFacturableIds.Contains(p.Id) &&
                     (f.Estado == EstadoTimbrado.Timbrado && f.FechaTimbrado != null || f.Estado == EstadoTimbrado.PendientePedimento)
               select new FacturaPedidoSalida(p.Id, p.ComportamientoFiscal == ComportamientoFiscal.ExportacionConCce,
                   f.Estado == EstadoTimbrado.PendientePedimento, f.FechaTimbrado)).ToListAsync(ct);

    public async Task<bool> CartaPorteVigenteAsync(Guid cartaPorteId, CancellationToken ct) =>
        await db.CartasPorte.AsNoTracking().AnyAsync(c => c.Id == cartaPorteId
            && c.Estado != EstadoTimbrado.Cancelado && c.Estado != EstadoTimbrado.Descartada && c.Estado != EstadoTimbrado.TimbradoFallido, ct);

    public async Task<DocumentoFiscalPt> EmitirNotaCreditoDevolucionAsync(Guid facturaId, IReadOnlyList<(int Posicion, int Piezas)> piezas, string descripcion, CancellationToken ct)
    {
        var factura = await db.FacturasVenta.AsNoTracking().Include(f => f.Lineas).FirstOrDefaultAsync(f => f.Id == facturaId, ct)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", "No existe la factura de las piezas.");
        decimal total = 0m;
        decimal? tasa = null;
        foreach (var (posicion, n) in piezas)
        {
            var linea = factura.Lineas.FirstOrDefault(l => l.Posicion == posicion && l.Cantidad > 0)
                ?? throw new BusinessRuleException("PT_NC_POSICION_SIN_FACTURA", $"La factura {factura.Folio} no trae la posición {posicion}.");
            if (n > linea.Cantidad) throw new BusinessRuleException("PT_NC_EXCEDE_FACTURADO", $"La posición {posicion} devuelve {n} piezas y la factura tiene {linea.Cantidad:0.##}.");
            // Por pieza: lo que la línea cobró (neto de descuento, con impuestos) entre su cantidad.
            total += (linea.Importe - linea.Descuento + linea.ImpuestoTrasladadoImporte - linea.RetencionTotalImporte) / linea.Cantidad * n;
            tasa ??= linea.TasaIvaTraslado;
        }
        var r = await sender.Send(new EmitirNotaCreditoBonificacionCommand(facturaId, Math.Round(total, 2, MidpointRounding.AwayFromZero), tasa, descripcion, Devolucion: true), ct);
        return new DocumentoFiscalPt(r.Id, r.Folio, r.Estado, r.Estado != nameof(EstadoTimbrado.Timbrado));
    }

    public async Task<DocumentoFiscalPt> SolicitarCancelacionFacturaAsync(Guid facturaId, string motivoSat, string? uuidSustituto, CancellationToken ct)
    {
        var r = await sender.Send(new SolicitarCancelacionCommand(facturaId, motivoSat, uuidSustituto), ct);
        var folio = await db.FacturasVenta.AsNoTracking().Where(f => f.Id == facturaId).Select(f => f.Folio).FirstOrDefaultAsync(ct);
        return new DocumentoFiscalPt(facturaId, folio, r.EstadoComprobante, r.EstadoComprobante != nameof(EstadoTimbrado.Cancelado));
    }

    public async Task<DocumentoFiscalPt?> EstadoDocumentoAsync(bool notaCredito, Guid id, CancellationToken ct)
    {
        var d = notaCredito
            ? await db.NotasCredito.AsNoTracking().Where(n => n.Id == id).Select(n => new { n.Folio, n.Estado }).FirstOrDefaultAsync(ct)
            : await db.FacturasVenta.AsNoTracking().Where(f => f.Id == id).Select(f => new { f.Folio, f.Estado }).FirstOrDefaultAsync(ct);
        return d is null ? null : new DocumentoFiscalPt(id, d.Folio, d.Estado.ToString(), d.Estado != (notaCredito ? EstadoTimbrado.Timbrado : EstadoTimbrado.Cancelado));
    }
}
