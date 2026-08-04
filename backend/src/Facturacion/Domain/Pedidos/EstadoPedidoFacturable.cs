namespace Millet.Facturacion.Domain.Pedidos;

/// <summary>
/// Estado de un <see cref="PedidoFacturable"/> — <b>binario respecto a
/// facturación</b> (D19): no existe "parcialmente facturado". Las parciales son
/// pedidos discretos distintos (§8 levantamiento).
///
/// <para>El valor numérico (<c>short</c>) está fijo por ABI — agregar al final.</para>
/// </summary>
public enum EstadoPedidoFacturable : short
{
    /// <summary>Listo para facturar; única ventana de edición libre.</summary>
    Importado = 1,

    /// <summary>Soft-lock mientras un cajero lo factura (F3; reusa ISoftLockManager).</summary>
    Bloqueado = 2,

    /// <summary>Tiene un CFDI vigente. Al cancelar el CFDI vuelve a Importado (re-facturable).</summary>
    Facturado = 3,

    /// <summary>Cancelado (Cancelación de A+W sin CFDI, o anulado).</summary>
    Cancelado = 4,

    /// <summary>Falló validación en la ingesta (no aplica a Manual — la captura es interactiva).</summary>
    Excepcion = 5,
}
