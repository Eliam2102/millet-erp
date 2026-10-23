namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura de Compras. Null significa documento inexistente.
/// Devuelve el estado actual sin filtrarlo; el consumidor valida si admite la operación.
/// </summary>
public interface IComprasOcReadPort
{
    Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken cancellationToken);

    /// <summary>Resuelve folios en lote, sin filtrar por estado, para presentación histórica.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
        IReadOnlyCollection<Guid> ocIds,
        CancellationToken cancellationToken);

    /// <summary>Ids de las OC de un proveedor (cualquier estado), para filtrar recepciones por proveedor.</summary>
    Task<IReadOnlyCollection<Guid>> ObtenerIdsPorProveedorAsync(
        Guid proveedorId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<Guid>>(Array.Empty<Guid>());
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
    IReadOnlyList<OcLineaLectura> Lineas,
    Guid? SucursalDestinoId = null, bool EsImportacion = false,
    string? NumeroPedimento = null, string? NumeroBl = null, string? NumeroContenedor = null);

public sealed record OcLineaLectura(
    Guid LineaId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal CantidadSolicitada,
    decimal CantidadRecibida,
    decimal PrecioUnitarioMxn,
    Guid? CentroCostoId = null,
    decimal CantidadFacturada = 0m, bool EsActivoFijo = false, Guid? ClaseActivoId = null, string? FraccionArancelaria = null);

public sealed record RecepcionesPendientesLectura(int Total, int Parciales);

/// <summary>Lectura agregada de órdenes para el Inicio de Almacén, sin importes ni datos del proveedor.</summary>
public interface IComprasRecepcionesReadPort
{
    Task<RecepcionesPendientesLectura> ResumirRecepcionesAsync(IReadOnlyList<Guid>? sucursales, CancellationToken cancellationToken);
}
