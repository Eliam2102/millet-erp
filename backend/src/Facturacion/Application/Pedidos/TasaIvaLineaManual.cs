using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Pedidos;

/// <summary>
/// Resuelve la tasa de IVA a persistir en líneas de pedido MANUAL
/// (FAC-DET-PR2). Regla del owner: IVA del artículo
/// (<c>ProductoAw.TasaIvaTraslado</c>) con prioridad sobre el IVA default
/// de la empresa; sin ninguno de los dos, la línea queda sin tasa (el FE
/// usa su fallback local). Los pedidos A+W NO pasan por aquí — llevan la
/// tasa del documento origen.
/// </summary>
internal static class TasaIvaLineaManual
{
    /// <summary>
    /// Devuelve un resolver <c>productoId → tasa</c> con los masters ya
    /// consultados (un lookup por producto distinto) y el default de
    /// empresa cargado una sola vez.
    /// </summary>
    internal static async Task<Func<Guid?, decimal?>> CrearResolverAsync(
        IProductosReadPort productos,
        IEmpresaFiscalReadPort empresas,
        Guid empresaId,
        IEnumerable<Guid?> productoIds,
        CancellationToken cancellationToken)
    {
        var empresaDefault =
            (await empresas.ObtenerAsync(empresaId, cancellationToken))?.TasaIvaDefault;

        var tasasPorProducto = new Dictionary<Guid, decimal?>();
        foreach (var productoId in productoIds.OfType<Guid>().Distinct())
        {
            var master = await productos.ObtenerAsync(productoId, cancellationToken);
            tasasPorProducto[productoId] = master?.TasaIvaTraslado;
        }

        return productoId =>
            (productoId is Guid id ? tasasPorProducto.GetValueOrDefault(id) : null)
            ?? empresaDefault;
    }
}
