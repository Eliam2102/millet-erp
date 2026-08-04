namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura del catálogo de canales de venta
/// (<c>compartido.canales_venta</c>, dueño: Administración — FAC-ING-PR2).
/// Reemplaza al enum hardcodeado <c>CanalVenta</c>: los validators verifican
/// existencia + estatus activo, las queries resuelven el nombre para
/// display y el lookup de selectores lista los activos. Adapter real:
/// <c>CanalesVentaReadAdapter</c> sobre <c>CompartidoDbContext</c> (cero
/// escritura, mismo precedente que <c>IEmpresaFiscalReadPort</c>).
/// </summary>
public interface ICanalesVentaReadPort
{
    /// <summary>True si el canal existe y está activo (validación de captura/emisión).</summary>
    Task<bool> ExisteActivoAsync(short canalVentaId, CancellationToken cancellationToken);

    /// <summary>
    /// Nombre del canal sin filtrar por estatus (display de históricos:
    /// un pedido con canal ya desactivado sigue mostrando su nombre) o
    /// <c>null</c> si el id no existe en el catálogo.
    /// </summary>
    Task<string?> ObtenerNombreAsync(short canalVentaId, CancellationToken cancellationToken);

    /// <summary>Canales activos ordenados por id, para selectores de UI.</summary>
    Task<IReadOnlyList<CanalVentaLectura>> ListarActivosAsync(CancellationToken cancellationToken);
}

/// <summary>Proyección de lectura del canal de venta para selectores.</summary>
public sealed record CanalVentaLectura(short Id, string Nombre);
