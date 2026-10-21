namespace Millet.Compras.Domain.Oc;

/// <summary>Tipo de incidencia del proveedor sobre una línea de la OC (F1-COM-07).</summary>
public enum TipoIncidenciaProveedorOc : short
{
    /// <summary>El proveedor entregó menos de lo pedido.</summary>
    Faltante = 1,

    /// <summary>Llegaron piezas dañadas (quedan en revisión, G1.3).</summary>
    Danado = 2,

    /// <summary>La entrega no llegó en la fecha prometida.</summary>
    Atraso = 3,
}
