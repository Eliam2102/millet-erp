namespace Millet.Facturacion.Domain.Pedidos;

/// <summary>
/// Origen de un <see cref="PedidoFacturable"/> (§3.bis.1 diseño). Hay tres
/// orígenes de pedido; el Sistema de Salidas <b>no</b> es origen (aporta
/// pedimento, no pedidos).
///
/// <para>El valor numérico (<c>short</c>) está fijo por ABI — agregar al final.</para>
/// </summary>
public enum OrigenPedido : short
{
    /// <summary>Ingesta automática desde A+W (vista SQL + cola de solicitudes, F3).</summary>
    Aw = 1,

    /// <summary>Ingesta desde Planta Pintura (vista SQL, F10).</summary>
    PlantaPintura = 2,

    /// <summary>Captura manual en el ERP (F1-PR2). No pasa por vistas ni Hybrid Connection.</summary>
    Manual = 3,
}
