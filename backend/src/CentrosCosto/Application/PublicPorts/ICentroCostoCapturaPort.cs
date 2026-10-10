namespace Millet.CentrosCosto.Application.PublicPorts;

public sealed record CentroCostoOpcion(Guid Id, string Clave, string Nombre, int Nivel, bool Activo,
    Guid Dim1Id, Guid? Dim2Id);
public sealed record CentroCostoCaptura(CentroCostoOpcion? Heredado, bool PuedeElegir,
    CentroCostoOpcion? UnicaOpcion, string? Mensaje);

/// <summary>ADM08: herencia organizacional y selección con alcance del capturador.</summary>
public interface ICentroCostoCapturaPort
{
    Task<CentroCostoCaptura> ObtenerAsync(Guid sucursalId, Guid departamentoId, CancellationToken ct);
    Task<Guid> ResolverAsync(Guid sucursalId, Guid departamentoId, Guid? elegido, CancellationToken ct);
    Task<IReadOnlyList<CentroCostoOpcion>> BuscarAsync(string? q, bool abierto, CancellationToken ct);
}
