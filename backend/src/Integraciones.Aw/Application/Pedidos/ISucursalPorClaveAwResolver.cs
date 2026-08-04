namespace Millet.Integraciones.Aw.Application.Pedidos;

/// <summary>
/// Resuelve la sucursal del ERP a partir de la clave con la que A+W la
/// refiere (<c>vw_erp_pedido_cabecera.numero_sucursal</c>, p.ej. "CONKAL").
/// La relación vive en el catálogo <c>compartido.sucursales.clave_aw</c>,
/// editable en el admin — sustituye al diccionario de configuración
/// <c>MapeoSucursal</c> (rediseño 2026-07-07, doc integration/04 §4).
/// </summary>
public interface ISucursalPorClaveAwResolver
{
    /// <summary>
    /// Id de la sucursal ACTIVA cuya <c>clave_aw</c> machea (case-insensitive,
    /// trim) o <c>null</c> si no hay match — el caller manda el pedido a la
    /// bandeja de excepciones.
    /// </summary>
    Task<Guid?> ResolverAsync(string claveAw, CancellationToken cancellationToken);
}
