using MediatR;
using Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;

namespace Millet.Facturacion.Application.CartaPorte.CrearSiguienteTramo;

/// <summary>
/// Crea la Carta Porte del siguiente tramo (§4.6, invariante 11): una Carta Porte
/// nueva que referencia la previa (<c>CartaPortePreviaId</c>), heredando receptor,
/// pedido y mercancías, y pidiendo solo los datos del tramo nuevo (origen,
/// destino, vehículo, operador, fechas). No muta la previa.
/// </summary>
public sealed record CrearSiguienteTramoCommand(
    Guid CartaPortePreviaId,
    string TipoCfdi, // "T" | "I"
    Guid SucursalId,
    string Origen,
    string Destino,
    decimal DistanciaKm,
    Guid VehiculoId,
    Guid OperadorId,
    DateTimeOffset FechaSalida,
    DateTimeOffset FechaLlegadaEstimada,
    decimal MontoServicio,
    decimal? TasaIvaServicio,
    // F12-PR3: el tramo nuevo tiene origen/destino propios — domicilio SAT
    // (CP 3.1). Nullable por compatibilidad; el builder valida al timbrar real.
    string? OrigenCodigoPostal = null,
    string? OrigenEstado = null,
    string? DestinoCodigoPostal = null,
    string? DestinoEstado = null) : IRequest<EmitirCartaPorteResponse>;
