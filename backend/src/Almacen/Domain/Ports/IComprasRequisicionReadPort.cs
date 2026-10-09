namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura de Compras. Null significa documento inexistente.
/// Devuelve el estado actual sin filtrarlo; el consumidor valida si admite la operación.
/// </summary>
public interface IComprasRequisicionReadPort
{
    Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken cancellationToken);

    /// <summary>Resuelve folios en lote, sin filtrar por estado, para presentación histórica.</summary>
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
    Guid? ProyectoId,
    decimal CantidadDisponibleEntregar = 0m);
