namespace Millet.Facturacion.Domain.CartaPorte;

/// <summary>
/// M-354: modalidad con la que se recorre un tramo del traslado. Millet solo emite Carta Porte en vehículos propios y en
/// tramos dentro de México (respuesta de Comercialización, 11-nov): el tramo de un transportista tercero no emite Carta Porte
/// de Millet y registra el documento que ampara el traslado. El tramo marítimo de exportación no se modela: lo documentan las
/// líneas de transporte y no genera Carta Porte de Millet.
/// </summary>
public enum ModalidadTramo
{
    VehiculoPropio = 1,
    TransportistaTercero = 2,
}
