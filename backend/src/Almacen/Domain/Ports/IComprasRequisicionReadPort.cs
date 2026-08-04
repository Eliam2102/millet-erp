namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Compras para verificar que una RQ existe y
/// está aprobada antes de surtir una salida contra ella (01-diseno §6.1).
///
/// <para>
/// Lo invoca <c>SurtirSalidaCommand</c> (F4-PR1). Devuelve <c>null</c>
/// si la RQ no existe o no está en estado <c>Autorizada</c> / <c>EnSurtido</c>
/// (los estados que aceptan surtido; ver <c>ComprasRequisicionReadAdapter</c>).
/// Tras la conmutación de ADR-0043 #3, las entregas viven en <c>EnSurtido</c>.
/// </para>
/// </summary>
public interface IComprasRequisicionReadPort
{
    Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve en <b>batch</b> <c>rqId → folio</c> para <b>presentación</b>
    /// (mostrar el folio de la RQ en el detalle/comprobante de una salida).
    ///
    /// <para>
    /// A diferencia de <see cref="ObtenerAsync"/> —que gatea por estado porque
    /// valida si la RQ admite surtido— esta lectura es <b>state-agnostic</b>:
    /// devuelve el folio sin importar el estado del workflow (una salida
    /// histórica puede tener su RQ ya <c>Surtida</c>/<c>Cerrada</c> y aun así
    /// debe mostrar su folio). Las RQs no encontradas no aparecen en el
    /// diccionario (fallback al id en el handler). ADR-0042.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
        IReadOnlyCollection<Guid> rqIds,
        CancellationToken cancellationToken);
}

public sealed record RequisicionLectura(
    Guid Id,
    string Folio,
    Guid EmpresaId,
    Guid DepartamentoId,
    Guid? AlmacenDestinoId,
    Guid? PersonaDestinatariaId,
    string Estado,
    IReadOnlyList<RequisicionLineaLectura> Lineas);

public sealed record RequisicionLineaLectura(
    Guid LineaId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal CantidadSolicitada,
    decimal CantidadSurtida,
    Guid? CentroCostoId,
    Guid? ProyectoId);
