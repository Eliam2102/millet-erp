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
}

public sealed record SolicitudCartaPorte(
    Guid SucursalId, string Origen, string OrigenCodigoPostal, string OrigenEstado, string Destino, string DestinoCodigoPostal, string DestinoEstado, decimal DistanciaKm,
    Guid VehiculoId, Guid OperadorId, Guid? PedidoFacturableId, DateTimeOffset FechaSalida, IReadOnlyList<MercanciaSalida> Mercancias, decimal PesoKg);

public sealed record MercanciaSalida(string Descripcion);

public sealed record CartaPorteSalida(Guid Id, string Folio);

/// <summary><c>Registrada</c>: había salida con ese folio. <c>Descartada</c> o <c>EnRevision</c>: cómo quedó en Facturación.</summary>
public sealed record CancelacionSalidaPt(bool Registrada, bool Descartada, bool EnRevision);
