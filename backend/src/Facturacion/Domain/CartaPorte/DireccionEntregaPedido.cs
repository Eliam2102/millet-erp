using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Domain.CartaPorte;

/// <summary>
/// M-354: regla del destino de la Carta Porte según las direcciones del pedido en A+W (<c>BW_AUFTR_KOPF</c>): se entrega en
/// la dirección de entrega (<c>AL_*</c>) y, si su nombre (<c>AL_NAME1</c>) y su calle (<c>AL_STRASSE</c>) vienen vacíos, en la
/// principal (<c>AH_*</c>). Función pura.
/// </summary>
public static class DireccionEntregaPedido
{
    public static DireccionPedidoAw Resolver(DireccionesPedidoAw direcciones) =>
        string.IsNullOrWhiteSpace(direcciones.Entrega.Nombre) && string.IsNullOrWhiteSpace(direcciones.Entrega.Calle)
            ? direcciones.Principal
            : direcciones.Entrega;

    /// <summary>Texto del destino: «calle, ciudad, provincia, país» sin los vacíos; <c>null</c> si no hay nada.</summary>
    public static string? Texto(DireccionPedidoAw d)
    {
        var partes = new[] { d.Calle, d.Ciudad, d.Provincia, d.Pais }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim());
        var texto = string.Join(", ", partes);
        return texto.Length == 0 ? null : texto;
    }
}
