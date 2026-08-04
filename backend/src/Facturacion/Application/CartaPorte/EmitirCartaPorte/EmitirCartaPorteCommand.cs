using MediatR;

namespace Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;

/// <summary>
/// Emite (sella + timbra) una Carta Porte 3.1 (§4.6). <c>TipoCfdi</c> = "T"
/// (Traslado, mercancía propia, total 0) o "I" (Ingreso, factura el servicio de
/// transporte). Contra el stub de timbrado hasta F12.
/// </summary>
public sealed record EmitirCartaPorteCommand(
    Guid SucursalId,
    Guid? CajaId,
    string TipoCfdi, // "T" | "I"
    // Receptor
    string ReceptorRfc,
    string ReceptorNombre,
    string ReceptorRegimenFiscal,
    string ReceptorCodigoPostal,
    string ReceptorUsoCfdi,
    string ReceptorPais,
    // Emisor
    string RfcEmisor,
    string RegimenFiscalEmisor,
    string Moneda,
    // Tramo
    string Origen,
    string Destino,
    decimal DistanciaKm,
    Guid VehiculoId,
    Guid OperadorId,
    Guid? PedidoFacturableId,
    DateTimeOffset FechaSalida,
    DateTimeOffset FechaLlegadaEstimada,
    // Tipo I: servicio facturado
    decimal MontoServicio,
    decimal? TasaIvaServicio,
    IReadOnlyList<CartaPorteMercanciaInput> Mercancias,
    // F12-PR3: domicilio SAT de las ubicaciones (CP 3.1 exige Domicilio en
    // Origen/Destino; país siempre MEX en MVP). Nullable por compatibilidad
    // con el FE actual; el builder valida al timbrar real.
    string? OrigenCodigoPostal = null,
    string? OrigenEstado = null,
    string? DestinoCodigoPostal = null,
    string? DestinoEstado = null) : IRequest<EmitirCartaPorteResponse>;

public sealed record CartaPorteMercanciaInput(
    string Descripcion,
    string BienesTransp,
    string ClaveUnidad,
    decimal Cantidad,
    decimal PesoEnKg,
    bool MaterialPeligroso);

public sealed record EmitirCartaPorteResponse(
    Guid Id,
    string TipoCfdi,
    string Estado,
    string? Uuid,
    string Folio,
    decimal Total,
    Guid? CartaPortePreviaId);
