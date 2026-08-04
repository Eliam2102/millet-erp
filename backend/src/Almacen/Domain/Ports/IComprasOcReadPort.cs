namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia el módulo Compras para verificar que una OC
/// existe y está autorizada antes de aceptar una recepción contra ella
/// (01-diseno §6.1). Cero acceso directo a tablas <c>compras.*</c> desde
/// Almacén — solo este puerto.
///
/// <para>
/// Lo invocan los handlers de <c>RegistrarRecepcionConFacturaCommand</c>
/// (F2-PR2, Variante A) y <c>RegistrarRecepcionConPackingListCommand</c>
/// (F3-PR1, Variante B). Debe devolver <c>null</c> si la OC no existe o
/// está en estado distinto de Autorizada/Parcialmente recibida.
/// </para>
/// </summary>
public interface IComprasOcReadPort
{
    Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve en <b>batch</b> <c>ocId → folio</c> para <b>presentación</b>
    /// (mostrar el folio de la OC en el detalle y la bandeja de recepciones).
    ///
    /// <para>
    /// A diferencia de <see cref="ObtenerAsync"/> —que gatea por estado porque
    /// valida si la OC admite recepción— esta lectura es <b>state-agnostic</b>:
    /// devuelve el folio sin importar el estado del workflow (una recepción
    /// histórica puede tener su OC ya <c>Cerrada</c>/<c>Cancelada</c> y aun así
    /// debe mostrar su folio). Las OCs no encontradas no aparecen en el
    /// diccionario (fallback al id en el handler). ADR-0042 (2º caso
    /// cross-módulo state-agnostic, espejo del folio de RQ).
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
        IReadOnlyCollection<Guid> ocIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Proyección read-only de una OC para Almacén. Solo los campos
/// necesarios para validar recepción + calcular costo de inventario.
/// </summary>
public sealed record OcLectura(
    Guid Id,
    string Folio,
    Guid ProveedorId,
    Guid EmpresaId,
    string Estado,
    IReadOnlyList<OcLineaLectura> Lineas);

public sealed record OcLineaLectura(
    Guid LineaId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal CantidadSolicitada,
    decimal CantidadRecibida,
    decimal PrecioUnitarioMxn);
