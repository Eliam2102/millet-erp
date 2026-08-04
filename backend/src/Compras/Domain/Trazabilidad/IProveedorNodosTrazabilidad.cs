namespace Millet.Compras.Domain.Trazabilidad;

/// <summary>
/// Provider que aporta nodos descendentes al árbol de trazabilidad
/// cross-módulo desde un módulo distinto a Compras. Cada módulo registra
/// su propia implementación (Almacén → recepciones, CxP → facturas,
/// Tesorería → pagos cuando exista).
///
/// <para>
/// El servicio <see cref="IObtenerArbolDocumentosService"/> consulta
/// todos los providers registrados al armar el árbol — cada provider
/// devuelve los nodos que conoce para el origen dado. Si un provider no
/// tiene nada que aportar para el tipo/id solicitado, retorna lista
/// vacía (no <c>null</c>).
/// </para>
///
/// <para>
/// <b>Bounded contexts</b>: el provider vive en el módulo dueño de los
/// datos (ej. <c>AlmacenRecepcionesTrazabilidadProvider</c> en Compras
/// pero queryeando <c>AlmacenDbContext</c> via PublicAdapter — o en el
/// módulo origen directamente si referencia Compras, como el caso de
/// CxP). Compras NO importa entidades de otros módulos; solo devuelve
/// los nodos como <see cref="NodoArbolDocumento"/> (DTO neutral).
/// </para>
/// </summary>
public interface IProveedorNodosTrazabilidad
{
    /// <summary>
    /// Devuelve los nodos descendentes que este provider conoce para el
    /// documento origen. Lista vacía si no aplica.
    /// </summary>
    Task<IReadOnlyList<NodoArbolDocumento>> ObtenerDescendentesAsync(
        TipoDocumentoTrazabilidad tipoOrigen,
        Guid idOrigen,
        CancellationToken cancellationToken);
}
