namespace Millet.ProductoTerminado.Application.Ports;

/// <summary>
/// Puerto hacia Facturación (PT5.5): la salida real que PT emite alimenta la facturación del embarque y la Carta Porte.
/// Facturación lo implementa; PT nunca toca sus tablas.
/// </summary>
public interface IFacturacionSalidaPort
{
    /// <summary>Registra la salida parcial del pedido (idempotente por <paramref name="referencia"/>, el folio del albarán). Falla si excede lo pedido.</summary>
    Task RegistrarSalidaAsync(Guid pedidoFacturableId, string referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> lineas, CancellationToken ct);

    /// <summary>Emite la Carta Porte de traslado (mercancía propia) del tramo de la hoja de salida.</summary>
    Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte solicitud, CancellationToken ct);
}

public sealed record SolicitudCartaPorte(
    Guid SucursalId, string Origen, string OrigenCodigoPostal, string OrigenEstado, string Destino, string DestinoCodigoPostal, string DestinoEstado, decimal DistanciaKm,
    Guid VehiculoId, Guid OperadorId, Guid? PedidoFacturableId, DateTimeOffset FechaSalida, IReadOnlyList<MercanciaSalida> Mercancias, decimal PesoKg);

public sealed record MercanciaSalida(string Descripcion);

public sealed record CartaPorteSalida(Guid Id, string Folio);
