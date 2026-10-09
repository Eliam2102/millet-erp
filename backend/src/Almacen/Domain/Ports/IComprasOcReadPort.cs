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
