namespace Millet.Integraciones.Aw.Application.Pedidos;

/// <summary>
/// Resuelve el canal de venta del ERP a partir de la clave con la que A+W lo
/// refiere (<c>vw_erp_pedido_cabecera.canal_ventas</c> — GRUPPE crudo, p.ej.
/// "Ventas Cancun", "CC Mérida"). La relación vive en el catálogo
/// <c>compartido.canales_venta.clave_aw</c>, editable en el admin —
/// sustituye al parse de enum + diccionario <c>MapeoCanalVenta</c>
/// (FAC-ING-PR2, mismo rediseño que la sucursal en doc integration/04 §4).
/// </summary>
public interface ICanalVentaPorClaveAwResolver
{
    /// <summary>
    /// Id del canal ACTIVO cuya <c>clave_aw</c> machea (case-insensitive,
    /// trim) o <c>null</c> si no hay match — el caller manda el pedido a la
    /// bandeja de excepciones.
    /// </summary>
    Task<short?> ResolverAsync(string claveAw, CancellationToken cancellationToken);
}
