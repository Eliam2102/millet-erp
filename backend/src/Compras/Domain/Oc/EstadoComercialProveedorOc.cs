namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Estado comercial de la OC según la última respuesta del proveedor (F1-COM-11).
/// Independiente del estado de la OC y de los sub-estados de recepción, facturación y pago.
/// </summary>
public enum EstadoComercialProveedorOc : short
{
    /// <summary>El proveedor aún no ha respondido.</summary>
    SinRespuesta = 0,

    /// <summary>El proveedor confirmó lo pendiente en las fechas esperadas.</summary>
    Confirmada = 1,

    /// <summary>El proveedor confirmó, pero cantidades o fechas difieren de lo pendiente.</summary>
    ConfirmadaConDiferencias = 2,

    /// <summary>El proveedor rechazó la orden.</summary>
    Rechazada = 3,
}
