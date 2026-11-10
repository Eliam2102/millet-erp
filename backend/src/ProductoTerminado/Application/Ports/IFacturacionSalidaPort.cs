namespace Millet.ProductoTerminado.Application.Ports;

/// <summary>
/// Puerto hacia Facturación (PT5.5): la salida real que PT emite alimenta la facturación del embarque y la Carta Porte.
/// Facturación lo implementa; PT nunca toca sus tablas.
/// </summary>
public interface IFacturacionSalidaPort
{
    /// <summary>Registra la salida parcial del pedido (idempotente por <paramref name="referencia"/>, el folio del albarán). Falla si excede lo pedido.</summary>
    Task RegistrarSalidaAsync(Guid pedidoFacturableId, string referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> lineas, CancellationToken ct);

    /// <summary>
    /// FAC-08 Fase 2: cancela la salida de un albarán ya emitido (<paramref name="referencia"/> = folio del albarán). Sin factura vigente
    /// Facturación la descarta y lo embarcado vuelve a quedar pendiente; con factura vigente la conserva y la manda a revisión
    /// (bandeja de excepciones, «Cancelación sobre facturado»). Idempotente. PT aún no cancela hojas emitidas (S23): queda listo para PT5.8.
    /// </summary>
    Task<CancelacionSalidaPt> CancelarSalidaAsync(string referencia, string motivo, CancellationToken ct);

    /// <summary>Emite la Carta Porte de traslado (mercancía propia) del tramo de la hoja de salida.</summary>
    Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte solicitud, CancellationToken ct);

    /// <summary>
    /// M-177: factura timbrada de cada albarán (por folio) y lo que la caja cobra de ella (total neto de NC). Sin factura, el albarán no aparece.
    /// </summary>
    Task<IReadOnlyDictionary<string, FacturaAlbaranRuta>> FacturasDeAlbaranesAsync(IReadOnlyList<string> albaranFolios, CancellationToken ct);

    /// <summary>
    /// M-177: registra lo cobrado contra entrega en la sesión de caja abierta del usuario en <paramref name="sucursalId"/> (origen «Liquidación de
    /// ruta»), cada albarán sobre su factura. Todo o nada; una factura que ya tiene cobro de caja devuelve ese cobro sin cobrar otra vez.
    /// Falla sin sesión en la sucursal o con un albarán sin factura timbrada.
    /// </summary>
    Task<IReadOnlyList<CobroRutaRegistrado>> RegistrarCobrosRutaAsync(Guid sucursalId, IReadOnlyList<CobroRutaAlbaran> cobros, CancellationToken ct);

    /// <summary>
    /// Reglas de salida 1 y 3: facturas de venta de los pedidos que cuentan para emitir la hoja: timbradas (con su fecha de timbrado) o, en un
    /// pedido de exportación, retenidas en PendientePedimento. Sin factura así, el pedido no aparece.
    /// </summary>
    Task<IReadOnlyList<FacturaPedidoSalida>> FacturasDePedidosAsync(IReadOnlyList<Guid> pedidoFacturableIds, CancellationToken ct);
}

public sealed record FacturaPedidoSalida(Guid PedidoFacturableId, bool Exportacion, bool PendientePedimento, DateTimeOffset? FechaTimbrado);

public sealed record FacturaAlbaranRuta(string AlbaranFolio, Guid ComprobanteId, string Folio, decimal PorCobrar, Guid? CobroCajaId);

/// <summary><c>TipoTarjeta</c>: «Credito» o «Debito» (formas SAT 04 y 28).</summary>
public sealed record CobroRutaAlbaran(string AlbaranFolio, decimal Efectivo, decimal Voucher, string? VoucherReferencia, string? TipoTarjeta, string? TarjetaUltimos4);

public sealed record CobroRutaRegistrado(string AlbaranFolio, Guid CobroCajaId);

public sealed record SolicitudCartaPorte(
    Guid SucursalId, string Origen, string OrigenCodigoPostal, string OrigenEstado, string Destino, string DestinoCodigoPostal, string DestinoEstado, decimal DistanciaKm,
    Guid VehiculoId, Guid OperadorId, Guid? PedidoFacturableId, DateTimeOffset FechaSalida, IReadOnlyList<MercanciaSalida> Mercancias, decimal PesoKg);

public sealed record MercanciaSalida(string Descripcion);

public sealed record CartaPorteSalida(Guid Id, string Folio);

/// <summary><c>Registrada</c>: había salida con ese folio. <c>Descartada</c> o <c>EnRevision</c>: cómo quedó en Facturación.</summary>
public sealed record CancelacionSalidaPt(bool Registrada, bool Descartada, bool EnRevision);
