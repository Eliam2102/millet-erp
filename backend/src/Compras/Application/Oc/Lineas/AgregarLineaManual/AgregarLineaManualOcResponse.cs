namespace Millet.Compras.Application.Oc.Lineas.AgregarLineaManual;

/// <summary>
/// Respuesta de <see cref="AgregarLineaManualOcCommand"/>. Incluye los
/// campos críticos para que el cliente confirme la inserción
/// (id, posición, subtotal + iva post-recálculo).
/// </summary>
public sealed record AgregarLineaManualOcResponse(
    Guid LineaId,
    int Posicion,
    decimal SubtotalLinea,
    decimal IvaImporte);
