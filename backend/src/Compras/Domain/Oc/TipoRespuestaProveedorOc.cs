namespace Millet.Compras.Domain.Oc;

/// <summary>Tipo de respuesta del proveedor sobre una orden de compra autorizada (F1-COM-11).</summary>
public enum TipoRespuestaProveedorOc : short
{
    /// <summary>El proveedor confirma la orden, con una o varias fechas prometidas.</summary>
    Confirmacion = 1,

    /// <summary>El proveedor rechaza la orden, con motivo.</summary>
    Rechazo = 2,
}
