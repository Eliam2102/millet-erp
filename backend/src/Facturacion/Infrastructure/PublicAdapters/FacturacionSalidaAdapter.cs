using MediatR;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;
using Millet.Facturacion.Application.Pedidos.CancelarSalidaFacturable;
using Millet.Facturacion.Application.Pedidos.RegistrarSalidaFacturable;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Pedidos;
using Millet.ProductoTerminado.Application.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// PT5.5: la hoja de salida emitida por Producto Terminado es la salida real que Facturación factura (FAC-08), y su Carta Porte
/// sale con la funcionalidad existente. Reemplaza la simulación: ya no se registran a mano ni se esperan del stub.
/// </summary>
public sealed class FacturacionSalidaAdapter(ISender sender, IEmpresaFiscalReadPort empresas, ICurrentEmpresaContext empresa) : IFacturacionSalidaPort
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
}
